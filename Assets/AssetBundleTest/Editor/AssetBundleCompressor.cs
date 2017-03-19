using System;
using System.Linq;
using System.IO;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;

public static class AssetBundleCompressor
{
    public enum CompressMode
    {
        Raw = 0,
        Deflate,
        LZMA,
    };

    const int LZMA_DICT_SIZE = 1048576;

    public static void Compress (string inputPath, string[] names, string outputPath, CompressMode mode)
    {
        var targets = CreateTargets (names);
        var data = ReadFile (inputPath);
        var offsets = CreateOffsets (targets, data);
        var src_blocks = CreateSrcBlocks (offsets, data);
        var copy_blocks = CreateCopyBlocks (src_blocks, data);

        if (File.Exists (outputPath)) {
            File.Delete (outputPath);
        }
        using (var outputFile = new FileStream (outputPath, FileMode.CreateNew, FileAccess.Write)) {
            // header
            uint magic;
            switch (mode) {
            case CompressMode.Raw:
                magic = MAGIC_RAW;
                break;
            case CompressMode.Deflate:
                magic = MAGIC_DEFLATE;
                break;
            case CompressMode.LZMA:
                magic = MAGIC_LZMA;
                break;
            default:
                throw new Exception ("Unknown compress mode");
            }

            outputFile.Write (uint2bytes (magic), 0, 4);
            outputFile.Write (int2bytes (data.Length), 0, 4);

            // blocks
            switch (mode) {
            case CompressMode.Raw:
                WriteBlocksToStream (copy_blocks, outputFile, data);
                break;
            case CompressMode.Deflate:
                using (var zipstream = new System.IO.Compression.DeflateStream (outputFile, System.IO.Compression.CompressionMode.Compress, true)) {
                    WriteBlocksToStream (copy_blocks, zipstream, data);
                    zipstream.Close ();
                }
                break;
            case CompressMode.LZMA:
                using (var memstream = new MemoryStream ()) {
                    WriteBlocksToStream (copy_blocks, memstream, data);
                    memstream.Flush ();
                    var buf = memstream.ToArray ();
                    using (var m = new MemoryStream (buf)) {
                        SevenZip.Compression.LZMA.Encoder coder = new SevenZip.Compression.LZMA.Encoder ();
                        Int32 dictSize32 = (Int32)LZMA_DICT_SIZE;
                        coder.SetCoderProperties (new SevenZip.CoderPropID[] { SevenZip.CoderPropID.DictionarySize }, new object[] { dictSize32 });
                        coder.WriteCoderProperties (outputFile);
                        outputFile.Write (BitConverter.GetBytes (buf.Length), 0, 4);
                        coder.Code (m, outputFile, buf.Length, -1, null);
                    }
                }
                break;
            }

            var output_length = outputFile.Length;
            outputFile.Flush ();
            outputFile.Close ();

            Debug.LogFormat ("Reducled to {0:F2}% | {1}:{2} => {3}:{4}",
                100.0f * output_length / data.Length,
                inputPath, data.Length,
                outputPath, output_length);
        }
    }

    public static void Decompress (string inputPath, string outputPath)
    {
        byte[] data;
        int orig_size;
        long archive_size;
        using (var inputFile = new FileStream (inputPath, FileMode.Open, FileAccess.Read)) {
            var buffer = new byte[4];
            inputFile.Read (buffer, 0, 4);
            var magic = bytes2uint (buffer, 0);
            inputFile.Read (buffer, 0, 4);
            orig_size = bytes2int (buffer, 0);

            switch (magic) {
            case MAGIC_RAW:
                data = new byte[inputFile.Length - 8];
                inputFile.Read (data, 0, data.Length);
                break;
            case MAGIC_DEFLATE:
                data = DeflateData (inputFile);
                break;
            case MAGIC_LZMA:
                data = DecompressLZMA (inputFile);
                break;
            default:
                throw new Exception ("Unknown header");
            }
            archive_size = inputFile.Length;
            inputFile.Close ();
        }

        var buf = ExpandBlocks (data, orig_size);
        WriteFile (outputPath, buf);

        Debug.LogFormat ("Inflate {0}:{1} => {2}:{3}",
            inputPath, archive_size,
            outputPath, buf.Length);
    }

    const uint MAGIC_RAW = 0x99ee0000U;
    const uint MAGIC_DEFLATE = 0x99ee0001U;
    const uint MAGIC_LZMA = 0x99ee0002U;

    // http://stackoverflow.com/questions/9889427/search-longest-pattern-in-byte-array-in-c-sharp/9890164#9890164
    static int SimpleBoyerMooreSearch (byte[] haystack, int offset, byte[] needle)
    {
        int[] lookup = new int[256];
        for (int i = 0; i < lookup.Length; i++) {
            lookup [i] = needle.Length;
        }

        for (int i = 0; i < needle.Length; i++) {
            lookup [needle [i]] = needle.Length - i - 1;
        }

        int index = offset + needle.Length - 1;
        var lastByte = needle.Last ();
        while (index < haystack.Length) {
            var checkByte = haystack [index];
            if (haystack [index] == lastByte) {
                bool found = true;
                for (int j = needle.Length - 2; j >= 0; j--) {
                    if (haystack [index - needle.Length + j + 1] != needle [j]) {
                        found = false;
                        break;
                    }
                }

                if (found)
                    return index - needle.Length + 1;
                else
                    index++;
            } else {
                index += lookup [checkByte];
            }
        }
        return -1;
    }

    //

    static byte[] int2bytes (int value)
    {
        return uint2bytes ((uint)value);
    }

    static byte[] uint2bytes (uint value)
    {
        var bytes = new byte[4];
        bytes [0] = (byte)((value >> 0) & 0xff);
        bytes [1] = (byte)((value >> 8) & 0xff);
        bytes [2] = (byte)((value >> 16) & 0xff);
        bytes [3] = (byte)((value >> 24) & 0xff);
        return bytes;
    }

    static int bytes2int (byte[] buffer, int offset)
    {
        return (int)bytes2uint (buffer, offset);
    }

    static uint bytes2uint (byte[] buffer, int offset)
    {
        return ((uint)buffer [offset]) |
        (((uint)buffer [offset + 1]) << 8) |
        (((uint)buffer [offset + 2]) << 16) |
        (((uint)buffer [offset + 3]) << 24);
    }

    static byte[] ReadFile (string path)
    {
        using (var inputFile = new FileStream (path, FileMode.Open, FileAccess.Read)) {
            var data = new byte[inputFile.Length];
            inputFile.Read (data, 0, data.Length);
            inputFile.Close ();
            return data;
        }
    }

    static void WriteFile (string path, byte[] buf)
    {
        if (File.Exists (path)) {
            File.Delete (path);
        }
        using (var outputFile = new FileStream (path, FileMode.CreateNew, FileAccess.Write)) {
            outputFile.Write (buf, 0, buf.Length);
            outputFile.Close ();
        }
    }

    class ByteAndStringName
    {
        public string BaseName;
        public byte[] Bytes;
        public List<int> Offsets;
    }

    class SrcBlock
    {
        public int Start;
        public int End;

        public int Length {
            get {
                return End - Start;
            }
        }
    }

    class CopyBlock
    {
        public int SrcOffset;
        public int DstOffset;
        public int Length;
    }

    static ByteAndStringName[] CreateTargets (string[] names)
    {
        return names
            .Select (v => Path.GetFileNameWithoutExtension (v))
            .Distinct ()
            .Select (v => {
            var name_bytes = System.Text.Encoding.ASCII.GetBytes (v);
            var name_len = name_bytes.Length;
            var result = new ByteAndStringName () {
                BaseName = v,
                Bytes = new byte[name_len + 4],
                Offsets = new List<int> (),
            };
            result.Bytes [0] = (byte)((name_len >> 0) & 0xff);
            result.Bytes [1] = (byte)((name_len >> 8) & 0xff);
            result.Bytes [2] = (byte)((name_len >> 16) & 0xff);
            result.Bytes [3] = (byte)((name_len >> 24) & 0xff);
            Array.Copy (name_bytes, 0, result.Bytes, 4, name_len);
            return result;
        })
            .ToArray ();
    }

    static List<int> CreateOffsets (ByteAndStringName[] targets, byte[] data)
    {
        var offsets = new List<int> ();
        foreach (var i in targets) {
            var last_offset = 0;
            while (true) {
                var offset = SimpleBoyerMooreSearch (data, last_offset, i.Bytes);
                if (offset < 0) {
                    break;
                }
                offset += i.Bytes.Length;
                i.Offsets.Add (offset);
                offsets.Add (offset);
                last_offset = offset;
            }
        }
        offsets.Sort ();
        return offsets;
    }

    static List<SrcBlock> CreateSrcBlocks (List<int> offsets, byte[] data)
    {
        var src_blocks = new List<SrcBlock> ();
        for (var i = 0; i < offsets.Count; i++) {
            int end;
            if (i == offsets.Count - 1) {
                end = data.Length; 
            } else {
                end = offsets [i + 1];
            }
            src_blocks.Add (new SrcBlock () {
                Start = offsets [i],
                End = end,
            });
        }
        return src_blocks;
    }

    static List<CopyBlock> CreateCopyBlocks (List<SrcBlock> src_blocks, byte[] data)
    {
        // copyが一つ定義されるたびに 12byte 増加する。
        // コピー長は最低16byteとする。
        const int MIN_COPY_LENGTH = 16;

        var copy_blocks = new List<CopyBlock> ();
        for (var i = 0; i < src_blocks.Count - 1; i++) {
            for (var j = i + 1; j < src_blocks.Count; j++) {
                var i_len = src_blocks [i].Length;
                var j_len = src_blocks [j].Length;
                var len = i_len > j_len ? j_len : i_len;

                var i_ofs = src_blocks [i].Start;
                var j_ofs = src_blocks [j].Start;

                var same_start = -1;
                for (var k = 0; k < len; k++) {
                    if (data [i_ofs + k] == data [j_ofs + k]) {
                        if (same_start == -1) {
                            same_start = k;
                        }
                    } else {
                        if (same_start != -1) {
                            var same_len = k - same_start;
                            if (same_len >= MIN_COPY_LENGTH) {
                                copy_blocks.Add (new CopyBlock () {
                                    SrcOffset = i_ofs + same_start,
                                    DstOffset = j_ofs + same_start,
                                    Length = same_len,
                                });
                            }
                            same_start = -1;
                        }
                    }
                }
            }
        }

        // 同じ領域にコピーされる部分を削除する
        var watchdog = copy_blocks.Count * 3;
        copy_blocks.Sort ((a, b) => a.DstOffset - b.DstOffset);
        for (var i = 1; i < copy_blocks.Count;) {
            watchdog--;
            if (watchdog < 0) {
                throw new Exception ("Watchdog stopped");
            }
            var prev = copy_blocks [i - 1];
            var cur = copy_blocks [i];
            var collLength = prev.DstOffset + prev.Length - cur.DstOffset;
            if (collLength <= 0) {
                i++;
                continue;
            }
            // 重複領域あり、再ソートしてやりなおす。
            bool trimCur;
            if (prev.Length == cur.Length) {
                // コピー長が同じならソースオフセットが大きい方を優先 (距離が近い)
                if (prev.SrcOffset > cur.SrcOffset) {
                    trimCur = false;
                } else {
                    trimCur = true;
                }
            } else if (prev.Length > cur.Length) {
                // 元のコピー長が長い方を優先
                trimCur = true;
            } else {
                // 元のコピー長が長い方を優先
                trimCur = false;
            }
            if (trimCur) {
                cur.SrcOffset += collLength;
                cur.DstOffset += collLength;
                cur.Length -= collLength;
                if (cur.Length < MIN_COPY_LENGTH) {
                    copy_blocks.RemoveAt (i);
                } else {
                    copy_blocks.Sort ((a, b) => a.DstOffset - b.DstOffset);
                }
            } else {
                prev.Length -= collLength;
                if (prev.Length < MIN_COPY_LENGTH) {
                    copy_blocks.RemoveAt (i - 1);
                    i--;
                    if (i < 1) {
                        i = 1;
                    }
                }
            }
        }

        return copy_blocks;
    }

    // Output structure
    // Save : 0, 31bit(Length) : Data
    // Copy : 1, 31bit(Length) : 32bit(SrcPosition)

    static void WriteBlocksToStream (List<CopyBlock> copy_blocks, Stream stream, byte[] data)
    {
        int current = 0;
        foreach (var i in copy_blocks) {
            var save_len = i.DstOffset - current;
            if (save_len > 0 || true) {
                stream.Write (int2bytes (save_len), 0, 4);
                stream.Write (data, current, save_len);
            }
            stream.Write (uint2bytes (((uint)i.Length) | 0x80000000U), 0, 4);
            stream.Write (int2bytes (i.SrcOffset), 0, 4);
            current = i.DstOffset + i.Length;

            // check
            for (var j = 0; j < i.Length; j++) {
                Assert.AreEqual (data [i.SrcOffset + j], data [i.DstOffset + j]);
            }
        }
        if (current < data.Length) {
            var save_len = data.Length - current;
            stream.Write (int2bytes (save_len), 0, 4);
            stream.Write (data, current, save_len);
        }
    }

    static byte[] DeflateData (Stream stream)
    {
        using (var output = new MemoryStream ()) {
            using (var deflateStream = new System.IO.Compression.DeflateStream (stream, System.IO.Compression.CompressionMode.Decompress, true)) {
                var buffer = new byte[4096];
                int readBytes;
                while ((readBytes = deflateStream.Read (buffer, 0, buffer.Length)) > 0) {
                    output.Write (buffer, 0, readBytes);
                }
                return output.ToArray ();
            }
        }
    }

    static byte[] DecompressLZMA (Stream stream)
    {
        using (var output = new MemoryStream ()) {
            SevenZip.Compression.LZMA.Decoder coder = new SevenZip.Compression.LZMA.Decoder ();
            byte[] properties = new byte[5]; // kPropSize (LzmaEncoder.cs)
            stream.Read (properties, 0, 5);
            byte[] fileLengthBytes = new byte[4];
            stream.Read (fileLengthBytes, 0, 4);
            long fileLength = BitConverter.ToInt32 (fileLengthBytes, 0);
            coder.SetDecoderProperties (properties);
            coder.Code (stream, output, stream.Length, fileLength, null);
            output.Flush ();
            var data = output.ToArray ();
            output.Close ();
            return data;
        }
    }

    static byte[] ExpandBlocks (byte[] data, int size)
    {
        var current = 0;
        var buf = new byte[size];
        var buf_offset = 0;
        while (current < data.Length) {
            var cmd = bytes2uint (data, current);
            current += 4;
            if ((cmd & 0x80000000U) == 0) {
                var len = (int)cmd;
                Array.Copy (data, current, buf, buf_offset, len);
                buf_offset += len;
                current += len;
            } else {
                var len = (int)(cmd & ~0x80000000U);
                var src = bytes2int (data, current);
                current += 4;
                Array.Copy (buf, src, buf, buf_offset, len);
                buf_offset += len;
            }
        }

        Assert.AreEqual (current, data.Length);
        Assert.AreEqual (buf_offset, buf.Length);
        Assert.AreEqual (buf_offset, size);

        return buf;
    }

}
