using System.Buffers.Binary;

namespace ClaudeMultiAccountLauncher.Core.Shortcuts;

/// <summary>
/// Builds a .ico file from an exe's RT_GROUP_ICON directory and its RT_ICON images.
/// The group entry layout matches the .ico entry except the last field: a 2-byte resource
/// id instead of a 4-byte file offset.
/// </summary>
public static class IcoFile
{
    private const int HeaderSize = 6;
    private const int GroupEntrySize = 14;
    private const int FileEntrySize = 16;

    /// <param name="groupDirectory">Raw RT_GROUP_ICON resource.</param>
    /// <param name="loadImage">Returns the RT_ICON resource for a resource id.</param>
    public static byte[] FromResourceGroup(ReadOnlySpan<byte> groupDirectory, Func<ushort, byte[]> loadImage)
    {
        if (groupDirectory.Length < HeaderSize || BinaryPrimitives.ReadUInt16LittleEndian(groupDirectory[2..]) != 1)
        {
            throw new InvalidDataException("Not an icon group resource.");
        }

        int count = BinaryPrimitives.ReadUInt16LittleEndian(groupDirectory[4..]);
        if (count == 0 || groupDirectory.Length < HeaderSize + count * GroupEntrySize)
        {
            throw new InvalidDataException("Icon group resource is truncated.");
        }

        var images = new byte[count][];
        for (var i = 0; i < count; i++)
        {
            var id = BinaryPrimitives.ReadUInt16LittleEndian(groupDirectory[(HeaderSize + i * GroupEntrySize + 12)..]);
            images[i] = loadImage(id);
        }

        var output = new byte[HeaderSize + count * FileEntrySize + images.Sum(img => img.Length)];
        var span = output.AsSpan();
        groupDirectory[..HeaderSize].CopyTo(span); // reserved, type=1, count

        var offset = HeaderSize + count * FileEntrySize;
        for (var i = 0; i < count; i++)
        {
            var entry = span[(HeaderSize + i * FileEntrySize)..];
            groupDirectory.Slice(HeaderSize + i * GroupEntrySize, 8).CopyTo(entry); // w, h, colors, reserved, planes, bpp
            BinaryPrimitives.WriteUInt32LittleEndian(entry[8..], (uint)images[i].Length);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[12..], (uint)offset);
            images[i].CopyTo(span[offset..]);
            offset += images[i].Length;
        }

        return output;
    }

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// Builds a .ico whose images are PNG files, stored as they are (supported since Windows
    /// Vista). Used for Store apps such as WhatsApp, whose icons are PNGs, not exe resources.
    /// </summary>
    public static byte[] FromPngs(IReadOnlyList<byte[]> pngs)
    {
        if (pngs.Count == 0)
        {
            throw new InvalidDataException("No images for the icon.");
        }

        var offset = HeaderSize + (FileEntrySize * pngs.Count);
        var output = new byte[offset + pngs.Sum(p => p.Length)];
        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(2), 1); // type: icon
        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(4), (ushort)pngs.Count);

        for (var i = 0; i < pngs.Count; i++)
        {
            var png = pngs[i];
            if (png.Length < 24 || !png.AsSpan(0, 8).SequenceEqual(PngSignature))
            {
                throw new InvalidDataException("Not a PNG image.");
            }

            // IHDR width and height, big-endian; 256 is written as 0
            var width = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16));
            var height = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20));
            var entry = output.AsSpan(HeaderSize + (FileEntrySize * i), FileEntrySize);
            entry[0] = width >= 256 ? (byte)0 : (byte)width;
            entry[1] = height >= 256 ? (byte)0 : (byte)height;
            BinaryPrimitives.WriteUInt16LittleEndian(entry[4..], 1); // planes
            BinaryPrimitives.WriteUInt16LittleEndian(entry[6..], 32); // bits per pixel
            BinaryPrimitives.WriteUInt32LittleEndian(entry[8..], (uint)png.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[12..], (uint)offset);
            png.CopyTo(output, offset);
            offset += png.Length;
        }

        return output;
    }
}
