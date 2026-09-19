using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.ValueObjects;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AvaloniaCadEditor.Application.Services;

public class ProjectRepository
{
    private readonly JsonSerializerOptions _options;

    public ProjectRepository()
    {
        _options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public void SaveToFile(Sketch sketch, string filePath)
    {
        var dto = new SketchDto
        {
            Entities = sketch.Entities.Select(MapToDto).ToList()
        };

        var json = JsonSerializer.Serialize(dto, _options);
        File.WriteAllText(filePath, json);
    }

    public Sketch LoadFromFile(string filePath)
    {
        var json = File.ReadAllText(filePath);
        var dto = JsonSerializer.Deserialize<SketchDto>(json, _options);
        
        var sketch = new Sketch();
        if (dto?.Entities != null)
        {
            foreach (var entityDto in dto.Entities)
            {
                var entity = MapFromDto(entityDto);
                if (entity != null)
                {
                    sketch.AddEntity(entity);
                }
            }
        }
        return sketch;
    }

    private EntityDto MapToDto(Entity entity)
    {
        if (entity is LineEntity line)
        {
            return new EntityDto { Type = "Line", StartX = line.StartPoint.X, StartY = line.StartPoint.Y, EndX = line.EndPoint.X, EndY = line.EndPoint.Y };
        }
        if (entity is CircleEntity circle)
        {
            return new EntityDto { Type = "Circle", StartX = circle.Center.X, StartY = circle.Center.Y, Radius = circle.Radius };
        }
        return new EntityDto { Type = "Unknown" };
    }

    private Entity? MapFromDto(EntityDto dto)
    {
        if (dto.Type == "Line")
        {
            return new LineEntity(new Point3D(dto.StartX, dto.StartY, 0), new Point3D(dto.EndX, dto.EndY, 0));
        }
        if (dto.Type == "Circle")
        {
            return new CircleEntity(new Point3D(dto.StartX, dto.StartY, 0), dto.Radius);
        }
        return null;
    }
}

public class SketchDto
{
    public List<EntityDto> Entities { get; set; } = new();
}

public class EntityDto
{
    public string Type { get; set; } = "";
    public double StartX { get; set; }
    public double StartY { get; set; }
    public double EndX { get; set; }
    public double EndY { get; set; }
    public double Radius { get; set; }
}
