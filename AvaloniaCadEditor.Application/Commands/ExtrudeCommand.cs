using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Commands;

public class ExtrudeCommand : ICommandTool
{
    public string Name => "Extrude";

    private enum State { SelectingProfile, DefiningDepth }
    private State _currentState = State.SelectingProfile;
    public bool IsDefiningDepth => _currentState == State.DefiningDepth;

    private IProfile? _selectedProfile;
    public IProfile? SelectedProfile => _selectedProfile;
    public double CurrentDepth { get; private set; } = 20.0;
    private Point3D _startPoint;
    public Point3D StartPointWorld { get; private set; }
    public bool IsFinished { get; private set; }
    
    public event EventHandler<string>? CodeGenerated;

    public void OnMouseDown(Sketch sketch, Point3D point)
    {
        if (_currentState == State.SelectingProfile)
        {
            // Pick profile closest to click or default to last profile
            _selectedProfile = FindProfileAtPoint(sketch, point) ?? sketch.Profiles.LastOrDefault();

            if (_selectedProfile != null)
            {
                _startPoint = point;
                _currentState = State.DefiningDepth;
            }
        }
        else if (_currentState == State.DefiningDepth)
        {
            if (_selectedProfile != null)
            {
                double depth = Math.Abs(CurrentDepth) < 1e-3 ? 20.0 : CurrentDepth;
                string code = GenerateExtrusionCode(_selectedProfile, depth, sketch);
                CodeGenerated?.Invoke(this, code);
                IsFinished = true;
            }
            Cancel();
        }
    }

    public void StartDirectExtrusion(Sketch sketch, IProfile profile, Point3D startPoint)
    {
        _selectedProfile = profile;
        _startPoint = startPoint;
        StartPointWorld = startPoint;
        CurrentDepth = 20.0;
        _currentState = State.DefiningDepth;
    }

    public void OnMouseMove(Sketch sketch, Point3D point)
    {
        if (_currentState == State.DefiningDepth && _selectedProfile != null)
        {
            // Fallback for 2D extrusion drag
            double dx = point.X - _startPoint.X;
            double dy = point.Y - _startPoint.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            
            // Determine sign based on general drag direction
            double sign = (dx + dy) >= 0 ? 1.0 : -1.0;
            CurrentDepth = distance > 0.1 ? distance * sign : 20.0;
        }
    }

    public void UpdateDepth(double depth)
    {
        if (_currentState == State.DefiningDepth)
        {
            CurrentDepth = depth;
        }
    }

    public void Cancel()
    {
        _currentState = State.SelectingProfile;
        _selectedProfile = null;
        CurrentDepth = 20.0;
        _previewFeature = null;
    }

    private ExtrudeFeature? _previewFeature;

    public IFeature? GetPreviewFeature()
    {
        if (_currentState == State.DefiningDepth && _selectedProfile != null)
        {
            if (_previewFeature == null || _previewFeature.Profile != _selectedProfile)
            {
                _previewFeature = new ExtrudeFeature(_selectedProfile, CurrentDepth);
            }
            else
            {
                _previewFeature.Depth = CurrentDepth;
                _previewFeature.Direction = _selectedProfile.Normal * CurrentDepth; // Update direction based on depth
            }
            return _previewFeature;
        }
        _previewFeature = null;
        return null;
    }

    public IEnumerable<Entity> GetPreviewEntities()
    {
        if (_currentState == State.DefiningDepth && _selectedProfile != null)
        {
            var previewLines = new List<Entity>();
            var baseVerts = _selectedProfile.GetVertices();
            if (baseVerts.Count == 0) return previewLines; // FaceProfile might not have simple vertices for preview

            var dir = _selectedProfile.Normal * CurrentDepth;
            var plane = _selectedProfile.Plane;

            for (int i = 0; i < baseVerts.Count; i++)
            {
                var p1World = baseVerts[i];
                var p2World = baseVerts[(i + 1) % baseVerts.Count];

                var top1World = new Point3D(p1World.X + dir.X, p1World.Y + dir.Y, p1World.Z + dir.Z);
                var top2World = new Point3D(p2World.X + dir.X, p2World.Y + dir.Y, p2World.Z + dir.Z);

                var p1Local = plane.ToLocalSpace(p1World);
                var top1Local = plane.ToLocalSpace(top1World);
                var top2Local = plane.ToLocalSpace(top2World);

                previewLines.Add(new LineEntity(top1Local, top2Local) { Plane = plane });
                
                // For vertical lines, just draw at corners, but maybe all of them?
                int step = Math.Max(1, baseVerts.Count / 8);
                if (i % step == 0)
                {
                    previewLines.Add(new LineEntity(p1Local, top1Local) { Plane = plane });
                }
            }

            return previewLines;
        }

        return Enumerable.Empty<Entity>();
    }

    public void OnKeyDown(Sketch sketch, CadKey key)
    {
        if (key == CadKey.Escape)
        {
            Cancel();
        }
    }

    public void OnValueEntered(Sketch sketch, double value)
    {
        if (_selectedProfile == null)
        {
            _selectedProfile = sketch.Profiles.LastOrDefault(p => p.IsVisible);
        }

        if (_selectedProfile != null)
        {
            string code = GenerateExtrusionCode(_selectedProfile, value, sketch);
            CodeGenerated?.Invoke(this, code);
            IsFinished = true;
        }
        Cancel();
    }

    private string GenerateExtrusionCode(IProfile profile, double depth, Sketch? sketch)
    {
        var sb = new StringBuilder();
        string id = Guid.NewGuid().ToString("N").Substring(0, 4);
        string depthStr = depth.ToString(CultureInfo.InvariantCulture);

        if (profile is ParametricBoxFace boxFace)
        {
            string parentId = string.IsNullOrEmpty(boxFace.ParentBox.Name)
                ? $"feature_{boxFace.ParentBox.Id.ToString("N").Substring(0, 4)}"
                : boxFace.ParentBox.Name;

            string boxVar = $"box_{id}";
            string boolVar = $"boolOp_{id}";

            sb.AppendLine($"var {boxVar} = new BoxFeature(baseCorner: {parentId}.GetFaceCorner(FaceType.{boxFace.SelectedFace}), width: {parentId}.GetFaceWidth(FaceType.{boxFace.SelectedFace}), length: {parentId}.GetFaceLength(FaceType.{boxFace.SelectedFace}), height: {depthStr}) {{ Name = \"{boxVar}\" }};");
            sb.AppendLine($"{boxVar}.AlignToFace({parentId}, FaceType.{boxFace.SelectedFace});");
            sb.AppendLine($"AddFeature({boxVar});");
            sb.AppendLine($"var {boolVar} = new BooleanFeature(\"Push/Pull\", {(depth < 0 ? "BooleanOperationType.Subtract" : "BooleanOperationType.Union")}, {parentId}, new[] {{ {boxVar} }}) {{ Name = \"{boolVar}\" }};");
            sb.AppendLine($"AddFeature({boolVar});");
        }
        else if (profile is ParametricCylinderFace cylFace)
        {
            string parentId = string.IsNullOrEmpty(cylFace.ParentCylinder.Name)
                ? $"feature_{cylFace.ParentCylinder.Id.ToString("N").Substring(0, 4)}"
                : cylFace.ParentCylinder.Name;

            string cylVar = $"cylinder_{id}";
            string boolVar = $"boolOp_{id}";

            sb.AppendLine($"var {cylVar} = new CylinderFeature(globalBaseCenter: {parentId}.GetFaceCenter(FaceType.{cylFace.SelectedFace}), globalDirection: {parentId}.GetFaceNormal(FaceType.{cylFace.SelectedFace}), radius: {cylFace.ParentCylinder.Radius.ToString(CultureInfo.InvariantCulture)}, height: {depthStr}) {{ Name = \"{cylVar}\" }};");
            sb.AppendLine($"AddFeature({cylVar});");
            sb.AppendLine($"var {boolVar} = new BooleanFeature(\"Push/Pull\", {(depth < 0 ? "BooleanOperationType.Subtract" : "BooleanOperationType.Union")}, {parentId}, new[] {{ {cylVar} }}) {{ Name = \"{boolVar}\" }};");
            sb.AppendLine($"AddFeature({boolVar});");
        }
        else if (profile is CircleProfile circleProfile)
        {
            string cylVar = $"cylinder_{id}";
            var center = circleProfile.Plane.ToWorldSpace(circleProfile.Center);
            var normal = circleProfile.Normal;
            sb.AppendLine($"var {cylVar} = new CylinderFeature(globalBaseCenter: new Point3D({center.X.ToString(CultureInfo.InvariantCulture)}, {center.Y.ToString(CultureInfo.InvariantCulture)}, {center.Z.ToString(CultureInfo.InvariantCulture)}), globalDirection: new Vector3D({normal.X.ToString(CultureInfo.InvariantCulture)}, {normal.Y.ToString(CultureInfo.InvariantCulture)}, {normal.Z.ToString(CultureInfo.InvariantCulture)}), radius: {circleProfile.Radius.ToString(CultureInfo.InvariantCulture)}, height: {depthStr}) {{ Name = \"{cylVar}\" }};");
            sb.AppendLine($"AddFeature({cylVar});");
        }
        else if (profile is RectangleProfile rectProfile)
        {
            string boxVar = $"box_{id}";
            double cos = Math.Cos(rectProfile.RotationAngle);
            double sin = Math.Sin(rectProfile.RotationAngle);
            var dirX = rectProfile.Plane.XAxis * cos + rectProfile.Plane.YAxis * sin;
            var dirY = rectProfile.Plane.XAxis * (-sin) + rectProfile.Plane.YAxis * cos;
            var dirZ = rectProfile.Plane.Normal;
            
            double hw = rectProfile.Width / 2.0;
            double hh = rectProfile.Height / 2.0;
            double cx = -hw * cos - (-hh) * sin;
            double cy = -hw * sin + (-hh) * cos;
            var localCorner = new Point3D(rectProfile.Center.X + cx, rectProfile.Center.Y + cy, rectProfile.Center.Z);
            var corner = rectProfile.Plane.ToWorldSpace(localCorner);

            sb.AppendLine($"var {boxVar} = new BoxFeature(baseCorner: new Point3D({corner.X.ToString(CultureInfo.InvariantCulture)}, {corner.Y.ToString(CultureInfo.InvariantCulture)}, {corner.Z.ToString(CultureInfo.InvariantCulture)}), width: {rectProfile.Width.ToString(CultureInfo.InvariantCulture)}, length: {rectProfile.Height.ToString(CultureInfo.InvariantCulture)}, height: {depthStr}) {{ Name = \"{boxVar}\", DirectionX = new Vector3D({dirX.X.ToString(CultureInfo.InvariantCulture)}, {dirX.Y.ToString(CultureInfo.InvariantCulture)}, {dirX.Z.ToString(CultureInfo.InvariantCulture)}), DirectionY = new Vector3D({dirY.X.ToString(CultureInfo.InvariantCulture)}, {dirY.Y.ToString(CultureInfo.InvariantCulture)}, {dirY.Z.ToString(CultureInfo.InvariantCulture)}), DirectionZ = new Vector3D({dirZ.X.ToString(CultureInfo.InvariantCulture)}, {dirZ.Y.ToString(CultureInfo.InvariantCulture)}, {dirZ.Z.ToString(CultureInfo.InvariantCulture)}) }};");
            sb.AppendLine($"AddFeature({boxVar});");
        }
        else
        {
            var verts = profile.GetVertices();
            if (verts.Count > 0)
            {
                var localVerts = verts.Select(v => profile.Plane.ToLocalSpace(v)).ToList();
                double minX = localVerts.Min(v => v.X);
                double maxX = localVerts.Max(v => v.X);
                double minY = localVerts.Min(v => v.Y);
                double maxY = localVerts.Max(v => v.Y);
                double minZ = localVerts.Min(v => v.Z);
                double maxZ = localVerts.Max(v => v.Z);

                double width = Math.Max(0.1, maxX - minX);
                double length = Math.Max(0.1, maxY - minY);
                Point3D localCorner = new Point3D(minX, minY, minZ);
                Point3D corner = profile.Plane.ToWorldSpace(localCorner);

                string boxVar = $"box_{id}";
                var dirX = profile.Plane.XAxis;
                var dirY = profile.Plane.YAxis;
                var dirZ = profile.Plane.Normal;
                sb.AppendLine($"var {boxVar} = new BoxFeature(baseCorner: new Point3D({corner.X.ToString(CultureInfo.InvariantCulture)}, {corner.Y.ToString(CultureInfo.InvariantCulture)}, {corner.Z.ToString(CultureInfo.InvariantCulture)}), width: {width.ToString(CultureInfo.InvariantCulture)}, length: {length.ToString(CultureInfo.InvariantCulture)}, height: {depthStr}) {{ Name = \"{boxVar}\", DirectionX = new Vector3D({dirX.X.ToString(CultureInfo.InvariantCulture)}, {dirX.Y.ToString(CultureInfo.InvariantCulture)}, {dirX.Z.ToString(CultureInfo.InvariantCulture)}), DirectionY = new Vector3D({dirY.X.ToString(CultureInfo.InvariantCulture)}, {dirY.Y.ToString(CultureInfo.InvariantCulture)}, {dirY.Z.ToString(CultureInfo.InvariantCulture)}), DirectionZ = new Vector3D({dirZ.X.ToString(CultureInfo.InvariantCulture)}, {dirZ.Y.ToString(CultureInfo.InvariantCulture)}, {dirZ.Z.ToString(CultureInfo.InvariantCulture)}) }};");
                sb.AppendLine($"AddFeature({boxVar});");
            }
        }

        if (sketch != null && !(profile is FaceProfile || profile is ParametricBoxFace || profile is ParametricCylinderFace))
        {
            foreach (var entityId in profile.SourceEntityIds)
            {
                var entity = sketch.Entities.FirstOrDefault(e => e.Id == entityId);
                if (entity != null)
                {
                    sketch.RemoveEntity(entity);
                }
            }
        }

        return sb.ToString();
    }

    public string GetMeasurementText()
    {
        if (_currentState == State.DefiningDepth)
        {
            return $"Height: {CurrentDepth:F2}";
        }
        return "Click a Profile/Face or enter Height";
    }

    private static IProfile? FindProfileAtPoint(Sketch sketch, Point3D point)
    {
        return sketch.Profiles
            .Where(p => p.IsVisible && !(p is FaceProfile))
            .OrderBy(p => p.Center.DistanceTo(point))
            .FirstOrDefault();
    }
}
