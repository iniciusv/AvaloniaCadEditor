using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Domain.Mesh;

namespace AvaloniaCadEditor.Infrastructure.IO;

public class StlService : IStlService
{
    public async Task<TriangleMesh> ImportAsync(string filePath)
    {
        return await Task.Run(() => 
        {
            using var stream = File.OpenRead(filePath);
            if (stream.Length < 84)
                return TriangleMesh.Empty;

            byte[] header = new byte[80];
            stream.Read(header, 0, 80);
            
            using var reader = new BinaryReader(stream);
            uint numTriangles = reader.ReadUInt32();
            
            long expectedLength = 84 + (numTriangles * 50);
            if (stream.Length == expectedLength)
            {
                return ReadBinary(reader, numTriangles);
            }
            else
            {
                stream.Position = 0;
                using var sr = new StreamReader(stream);
                return ReadAscii(sr);
            }
        });
    }

    private TriangleMesh ReadBinary(BinaryReader reader, uint numTriangles)
    {
        var positions = new List<float>((int)(numTriangles * 9));
        var normals = new List<float>((int)(numTriangles * 9));
        var indices = new List<uint>((int)(numTriangles * 3));

        uint currentIndex = 0;
        for (uint i = 0; i < numTriangles; i++)
        {
            float nx = reader.ReadSingle();
            float ny = reader.ReadSingle();
            float nz = reader.ReadSingle();

            for (int v = 0; v < 3; v++)
            {
                positions.Add(reader.ReadSingle());
                positions.Add(reader.ReadSingle());
                positions.Add(reader.ReadSingle());
                
                normals.Add(nx);
                normals.Add(ny);
                normals.Add(nz);
                
                indices.Add(currentIndex++);
            }

            reader.ReadUInt16();
        }

        return new TriangleMesh(positions.ToArray(), normals.ToArray(), indices.ToArray());
    }

    private TriangleMesh ReadAscii(StreamReader reader)
    {
        var positions = new List<float>();
        var normals = new List<float>();
        var indices = new List<uint>();
        uint currentIndex = 0;
        float nx = 0, ny = 0, nz = 0;
        
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            line = line.Trim();
            if (line.StartsWith("facet normal", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 5)
                {
                    float.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out nx);
                    float.TryParse(parts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out ny);
                    float.TryParse(parts[4], NumberStyles.Any, CultureInfo.InvariantCulture, out nz);
                }
            }
            else if (line.StartsWith("vertex", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 4)
                {
                    if (float.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out float vx) &&
                        float.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out float vy) &&
                        float.TryParse(parts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out float vz))
                    {
                        positions.Add(vx);
                        positions.Add(vy);
                        positions.Add(vz);
                        normals.Add(nx);
                        normals.Add(ny);
                        normals.Add(nz);
                        indices.Add(currentIndex++);
                    }
                }
            }
        }
        
        return new TriangleMesh(positions.ToArray(), normals.ToArray(), indices.ToArray());
    }

    public async Task ExportAsync(TriangleMesh mesh, string filePath, bool asBinary = true)
    {
        await Task.Run(() => 
        {
            if (mesh == null || mesh.TriangleCount == 0) return;

            if (asBinary)
            {
                using var stream = File.Create(filePath);
                using var writer = new BinaryWriter(stream);
                
                byte[] header = new byte[80];
                Encoding.ASCII.GetBytes("Exported by AvaloniaCadEditor").CopyTo(header, 0);
                writer.Write(header);
                
                writer.Write((uint)mesh.TriangleCount);
                
                for (int i = 0; i < mesh.TriangleCount; i++)
                {
                    int i1 = (int)mesh.Indices[i * 3];
                    int i2 = (int)mesh.Indices[i * 3 + 1];
                    int i3 = (int)mesh.Indices[i * 3 + 2];

                    float nx = mesh.Normals[i1 * 3];
                    float ny = mesh.Normals[i1 * 3 + 1];
                    float nz = mesh.Normals[i1 * 3 + 2];

                    writer.Write(nx);
                    writer.Write(ny);
                    writer.Write(nz);

                    writer.Write(mesh.Positions[i1 * 3]);
                    writer.Write(mesh.Positions[i1 * 3 + 1]);
                    writer.Write(mesh.Positions[i1 * 3 + 2]);

                    writer.Write(mesh.Positions[i2 * 3]);
                    writer.Write(mesh.Positions[i2 * 3 + 1]);
                    writer.Write(mesh.Positions[i2 * 3 + 2]);

                    writer.Write(mesh.Positions[i3 * 3]);
                    writer.Write(mesh.Positions[i3 * 3 + 1]);
                    writer.Write(mesh.Positions[i3 * 3 + 2]);

                    writer.Write((ushort)0);
                }
            }
            else 
            {
                using var writer = new StreamWriter(filePath);
                writer.WriteLine("solid exported_mesh");
                for (int i = 0; i < mesh.TriangleCount; i++)
                {
                    int i1 = (int)mesh.Indices[i * 3];
                    int i2 = (int)mesh.Indices[i * 3 + 1];
                    int i3 = (int)mesh.Indices[i * 3 + 2];

                    float nx = mesh.Normals[i1 * 3];
                    float ny = mesh.Normals[i1 * 3 + 1];
                    float nz = mesh.Normals[i1 * 3 + 2];

                    writer.WriteLine($"  facet normal {nx.ToString("F6", CultureInfo.InvariantCulture)} {ny.ToString("F6", CultureInfo.InvariantCulture)} {nz.ToString("F6", CultureInfo.InvariantCulture)}");
                    writer.WriteLine("    outer loop");
                    writer.WriteLine($"      vertex {mesh.Positions[i1 * 3].ToString("F6", CultureInfo.InvariantCulture)} {mesh.Positions[i1 * 3 + 1].ToString("F6", CultureInfo.InvariantCulture)} {mesh.Positions[i1 * 3 + 2].ToString("F6", CultureInfo.InvariantCulture)}");
                    writer.WriteLine($"      vertex {mesh.Positions[i2 * 3].ToString("F6", CultureInfo.InvariantCulture)} {mesh.Positions[i2 * 3 + 1].ToString("F6", CultureInfo.InvariantCulture)} {mesh.Positions[i2 * 3 + 2].ToString("F6", CultureInfo.InvariantCulture)}");
                    writer.WriteLine($"      vertex {mesh.Positions[i3 * 3].ToString("F6", CultureInfo.InvariantCulture)} {mesh.Positions[i3 * 3 + 1].ToString("F6", CultureInfo.InvariantCulture)} {mesh.Positions[i3 * 3 + 2].ToString("F6", CultureInfo.InvariantCulture)}");
                    writer.WriteLine("    endloop");
                    writer.WriteLine("  endfacet");
                }
                writer.WriteLine("endsolid exported_mesh");
            }
        });
    }
}
