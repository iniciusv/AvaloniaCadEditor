using System;
using System.IO;
using System.IO.Compression;
using AvaloniaCadEditor.Domain.Mesh;

namespace AvaloniaCadEditor.Application.Library;

/// <summary>
/// Malha → imagem da miniatura, em CPU: vista isométrica, z-buffer e sombreado plano por triângulo, fundo
/// transparente. O app não tem renderização fora da tela (o viewport OpenGL pertence ao controle), e assim a
/// miniatura não depende de contexto GL e é testável. Puro.
/// </summary>
public static class ThumbnailRasterizer
{
    // Cor de base das peças (azul acinzentado do viewport) e luz vinda de cima, da frente e da esquerda.
    private const double BaseR = 0x9f, BaseG = 0xb4, BaseB = 0xc7;
    private static readonly (double X, double Y, double Z) Light = Normalize((-0.4, -0.6, 0.7));

    /// <summary>Pixels RGBA (linha a linha, de cima para baixo), <paramref name="size"/> × <paramref name="size"/>.</summary>
    public static byte[] Render(TriangleMesh mesh, int size = 128)
    {
        var rgba = new byte[size * size * 4];
        if (mesh.TriangleCount == 0) return rgba;

        // Isométrica: azimute 45° (de -X,-Y olhando para +X,+Y) e elevação de ~35°, Z para cima.
        const double cosA = 0.70710678, sinA = 0.70710678;
        const double cosE = 0.81649658, sinE = 0.57735027;
        int n = mesh.VertexCount;
        var sx = new double[n];
        var sy = new double[n];
        var sz = new double[n];
        double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
        var p = mesh.Positions;
        for (int i = 0; i < n; i++)
        {
            double x = p[3 * i], y = p[3 * i + 1], z = p[3 * i + 2];
            double right = x * cosA - y * sinA;
            double depthH = x * sinA + y * cosA;
            // A câmera olha de cima para baixo: o que está mais ao fundo sobe na tela, e o que está mais alto chega perto.
            double up = z * cosE + depthH * sinE;
            double depth = depthH * cosE - z * sinE; // maior = mais longe da câmera
            sx[i] = right;
            sy[i] = up;
            sz[i] = depth;
            minX = Math.Min(minX, right); maxX = Math.Max(maxX, right);
            minY = Math.Min(minY, up); maxY = Math.Max(maxY, up);
        }

        double margin = size * 0.06;
        double span = Math.Max(maxX - minX, maxY - minY);
        double scale = span > 1e-9 ? (size - 2 * margin) / span : 1;
        double offX = (size - (maxX - minX) * scale) / 2;
        double offY = (size - (maxY - minY) * scale) / 2;
        for (int i = 0; i < n; i++)
        {
            sx[i] = offX + (sx[i] - minX) * scale;
            sy[i] = size - (offY + (sy[i] - minY) * scale); // imagem: y para baixo
        }

        var zbuffer = new double[size * size];
        Array.Fill(zbuffer, double.MaxValue);
        var idx = mesh.Indices;
        for (int t = 0; t + 2 < idx.Length; t += 3)
        {
            int a = (int)idx[t], b = (int)idx[t + 1], c = (int)idx[t + 2];
            if (a >= n || b >= n || c >= n) continue;

            var normal = Normalize(Cross(
                (p[3 * b] - p[3 * a], p[3 * b + 1] - p[3 * a + 1], p[3 * b + 2] - p[3 * a + 2]),
                (p[3 * c] - p[3 * a], p[3 * c + 1] - p[3 * a + 1], p[3 * c + 2] - p[3 * a + 2])));
            // Malhas nem sempre têm o sentido dos triângulos consistente: o lado que a luz pega é o de |n·l|.
            double lambert = Math.Abs(normal.X * Light.X + normal.Y * Light.Y + normal.Z * Light.Z);
            double shade = 0.35 + 0.65 * lambert;
            byte r = (byte)Math.Min(255, BaseR * shade), g = (byte)Math.Min(255, BaseG * shade), bl = (byte)Math.Min(255, BaseB * shade);

            FillTriangle(rgba, zbuffer, size, sx, sy, sz, a, b, c, r, g, bl);
        }
        return rgba;
    }

    /// <summary>Codifica pixels RGBA como PNG (8 bits por canal, sem filtro, zlib do BCL).</summary>
    public static byte[] EncodePng(byte[] rgba, int width, int height)
    {
        using var output = new MemoryStream();
        output.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        var header = new byte[13];
        WriteBigEndian(header, 0, (uint)width);
        WriteBigEndian(header, 4, (uint)height);
        header[8] = 8;  // bits por canal
        header[9] = 6;  // RGBA
        WriteChunk(output, "IHDR", header);

        using (var raw = new MemoryStream())
        {
            using (var zlib = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
            {
                int stride = width * 4;
                for (int y = 0; y < height; y++)
                {
                    zlib.WriteByte(0); // filtro: nenhum
                    zlib.Write(rgba, y * stride, stride);
                }
            }
            WriteChunk(output, "IDAT", raw.ToArray());
        }

        WriteChunk(output, "IEND", Array.Empty<byte>());
        return output.ToArray();
    }

    private static void FillTriangle(byte[] rgba, double[] zbuffer, int size, double[] sx, double[] sy, double[] sz,
        int a, int b, int c, byte r, byte g, byte bl)
    {
        double area = (sx[b] - sx[a]) * (sy[c] - sy[a]) - (sy[b] - sy[a]) * (sx[c] - sx[a]);
        if (Math.Abs(area) < 1e-12) return;

        int x0 = Math.Max(0, (int)Math.Floor(Math.Min(sx[a], Math.Min(sx[b], sx[c]))));
        int x1 = Math.Min(size - 1, (int)Math.Ceiling(Math.Max(sx[a], Math.Max(sx[b], sx[c]))));
        int y0 = Math.Max(0, (int)Math.Floor(Math.Min(sy[a], Math.Min(sy[b], sy[c]))));
        int y1 = Math.Min(size - 1, (int)Math.Ceiling(Math.Max(sy[a], Math.Max(sy[b], sy[c]))));

        for (int y = y0; y <= y1; y++)
        {
            double py = y + 0.5;
            for (int x = x0; x <= x1; x++)
            {
                double px = x + 0.5;
                double w0 = ((sx[b] - px) * (sy[c] - py) - (sy[b] - py) * (sx[c] - px)) / area;
                double w1 = ((sx[c] - px) * (sy[a] - py) - (sy[c] - py) * (sx[a] - px)) / area;
                double w2 = 1 - w0 - w1;
                if (w0 < -1e-9 || w1 < -1e-9 || w2 < -1e-9) continue;

                double depth = w0 * sz[a] + w1 * sz[b] + w2 * sz[c];
                int pixel = y * size + x;
                if (depth >= zbuffer[pixel]) continue;
                zbuffer[pixel] = depth;
                rgba[4 * pixel] = r;
                rgba[4 * pixel + 1] = g;
                rgba[4 * pixel + 2] = bl;
                rgba[4 * pixel + 3] = 255;
            }
        }
    }

    private static (double X, double Y, double Z) Cross((double X, double Y, double Z) u, (double X, double Y, double Z) v) =>
        (u.Y * v.Z - u.Z * v.Y, u.Z * v.X - u.X * v.Z, u.X * v.Y - u.Y * v.X);

    private static (double X, double Y, double Z) Normalize((double X, double Y, double Z) v)
    {
        double length = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        return length < 1e-12 ? (0, 0, 1) : (v.X / length, v.Y / length, v.Z / length);
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        var length = new byte[4];
        WriteBigEndian(length, 0, (uint)data.Length);
        output.Write(length);

        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);

        uint crc = Crc32(typeBytes, 0xFFFFFFFFu);
        crc = Crc32(data, crc) ^ 0xFFFFFFFFu;
        var crcBytes = new byte[4];
        WriteBigEndian(crcBytes, 0, crc);
        output.Write(crcBytes);
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    private static uint Crc32(byte[] data, uint crc)
    {
        foreach (byte b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    private static void WriteBigEndian(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }
}
