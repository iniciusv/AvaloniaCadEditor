using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Services;

public static class FeatureCodeGenerator
{
    private static double Clean(double v)
    {
        double rounded = Math.Round(v, 5);
        return Math.Abs(rounded) < 1e-10 ? 0.0 : rounded;
    }
    private static Vector3D CleanV(Vector3D v) => new Vector3D(Clean(v.X), Clean(v.Y), Clean(v.Z));
    private static Point3D CleanP(Point3D p) => new Point3D(Clean(p.X), Clean(p.Y), Clean(p.Z));

    private static string F(double v) => Clean(v).ToString(CultureInfo.InvariantCulture);

    public static string GenerateExtrusionCode(IProfile profile, double height, Sketch? sketch)
    {
        var sb = new StringBuilder();
        string id = Guid.NewGuid().ToString("N").Substring(0, 4);
        string heightStr = F(height);

        if (profile is ParametricBoxFace boxFace)
        {
            string parentId = string.IsNullOrEmpty(boxFace.ParentBox.Name)
                ? $"feature_{boxFace.ParentBox.Id.ToString("N").Substring(0, 4)}"
                : boxFace.ParentBox.Name;

            string boxVar = $"box_{id}";
            string boolVar = $"boolOp_{id}";

            sb.AppendLine($"var {boxVar} = new BoxFeature(baseCorner: {parentId}.GetFaceCorner(FaceType.{boxFace.SelectedFace}), width: {parentId}.GetFaceWidth(FaceType.{boxFace.SelectedFace}), length: {parentId}.GetFaceLength(FaceType.{boxFace.SelectedFace}), height: {heightStr}) {{ Name = \"{boxVar}\" }};");
            sb.AppendLine($"{boxVar}.AlignToFace({parentId}, FaceType.{boxFace.SelectedFace});");
            sb.AppendLine($"AddFeature({boxVar});");
            sb.AppendLine($"var {boolVar} = new BooleanFeature(\"Push/Pull\", {(height < 0 ? "BooleanOperationType.Subtract" : "BooleanOperationType.Union")}, {parentId}, new[] {{ {boxVar} }}) {{ Name = \"{boolVar}\" }};");
            sb.AppendLine($"AddFeature({boolVar});");
        }
        else if (profile is ParametricCylinderFace cylFace)
        {
            string parentId = string.IsNullOrEmpty(cylFace.ParentCylinder.Name)
                ? $"feature_{cylFace.ParentCylinder.Id.ToString("N").Substring(0, 4)}"
                : cylFace.ParentCylinder.Name;

            string cylVar = $"cylinder_{id}";
            string boolVar = $"boolOp_{id}";

            sb.AppendLine($"var {cylVar} = new CylinderFeature(globalBaseCenter: {parentId}.GetFaceCenter(FaceType.{cylFace.SelectedFace}), globalDirection: {parentId}.GetFaceNormal(FaceType.{cylFace.SelectedFace}), radius: {F(cylFace.ParentCylinder.Radius)}, height: {heightStr}) {{ Name = \"{cylVar}\" }};");
            sb.AppendLine($"AddFeature({cylVar});");
            sb.AppendLine($"var {boolVar} = new BooleanFeature(\"Push/Pull\", {(height < 0 ? "BooleanOperationType.Subtract" : "BooleanOperationType.Union")}, {parentId}, new[] {{ {cylVar} }}) {{ Name = \"{boolVar}\" }};");
            sb.AppendLine($"AddFeature({boolVar});");
        }
        else if (profile is CircleProfile circleProfile)
        {
            string cylVar = $"cylinder_{id}";
            var center = CleanP(circleProfile.Plane.ToWorldSpace(circleProfile.Center));
            var normal = CleanV(circleProfile.Normal);
            sb.AppendLine($"var {cylVar} = new CylinderFeature(globalBaseCenter: new Point3D({F(center.X)}, {F(center.Y)}, {F(center.Z)}), globalDirection: new Vector3D({F(normal.X)}, {F(normal.Y)}, {F(normal.Z)}), radius: {F(circleProfile.Radius)}, height: {heightStr}) {{ Name = \"{cylVar}\" }};");
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

            double boxWidth = rectProfile.Width;
            double boxLength = rectProfile.Height;
            double boxHeight = height;

            NormalizeBox(ref corner, ref boxWidth, ref boxLength, ref boxHeight, ref dirX, ref dirY, ref dirZ);

            bool isDefaultDir(Vector3D v, Vector3D def) => Math.Abs(v.X - def.X) < 1e-6 && Math.Abs(v.Y - def.Y) < 1e-6 && Math.Abs(v.Z - def.Z) < 1e-6;
            bool defaults = isDefaultDir(dirX, Vector3D.UnitX) && isDefaultDir(dirY, Vector3D.UnitY) && isDefaultDir(dirZ, Vector3D.UnitZ);

            string baseCode = $"var {boxVar} = new BoxFeature(baseCorner: new Point3D({F(corner.X)}, {F(corner.Y)}, {F(corner.Z)}), width: {F(boxWidth)}, length: {F(boxLength)}, height: {F(boxHeight)}) {{ Name = \"{boxVar}\"";

            if (defaults)
            {
                sb.AppendLine(baseCode + " };");
            }
            else
            {
                sb.AppendLine(baseCode + $", DirectionX = new Vector3D({F(dirX.X)}, {F(dirX.Y)}, {F(dirX.Z)}), DirectionY = new Vector3D({F(dirY.X)}, {F(dirY.Y)}, {F(dirY.Z)}), DirectionZ = new Vector3D({F(dirZ.X)}, {F(dirZ.Y)}, {F(dirZ.Z)}) }};");
            }
            sb.AppendLine($"AddFeature({boxVar});");
        }
        else
        {
            // Handles PolygonProfile and general fallback
            var verts = profile.GetVertices();
            if (verts.Count > 0)
            {
                string prismVar = $"prism_{id}";

                var pts = string.Join(", ", verts.Select(v =>
                {
                    var localV = CleanP(profile.Plane.ToLocalSpace(v));
                    return $"new Point3D({F(localV.X)}, {F(localV.Y)}, {F(localV.Z)})";
                }));
                sb.AppendLine($"var {prismVar}_verts = new[] {{ {pts} }};");

                var o = CleanP(profile.Plane.Origin);
                var normal = CleanV(profile.Plane.Normal);
                var xAxis = CleanV(profile.Plane.XAxis);

                bool isDefaultPlane = o.DistanceTo(Point3D.Origin) < 1e-6 &&
                                      (normal - new Vector3D(0, 0, 1)).Length < 1e-6 &&
                                      (xAxis - new Vector3D(1, 0, 0)).Length < 1e-6;

                if (isDefaultPlane)
                {
                    sb.AppendLine($"var {prismVar} = new PrismFeature({prismVar}_verts, height: {heightStr}) {{ Name = \"{prismVar}\" }};");
                }
                else
                {
                    sb.AppendLine($"var {prismVar}_plane = new WorkPlane(new Point3D({F(o.X)}, {F(o.Y)}, {F(o.Z)}), new Vector3D({F(normal.X)}, {F(normal.Y)}, {F(normal.Z)}), new Vector3D({F(xAxis.X)}, {F(xAxis.Y)}, {F(xAxis.Z)}));");
                    sb.AppendLine($"var {prismVar} = new PrismFeature({prismVar}_verts, height: {heightStr}, plane: {prismVar}_plane) {{ Name = \"{prismVar}\" }};");
                }
                sb.AppendLine($"AddFeature({prismVar});");
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

    private static void NormalizeBox(ref Point3D corner, ref double width, ref double length, ref double height, ref Vector3D dirX, ref Vector3D dirY, ref Vector3D dirZ)
    {
        corner = CleanP(corner);
        dirX = CleanV(dirX);
        dirY = CleanV(dirY);
        dirZ = CleanV(dirZ);
        height = Clean(height);
        width = Clean(width);
        length = Clean(length);

        bool isAxisAligned(Vector3D v) 
        {
            int ones = 0;
            int zeros = 0;
            if (Math.Abs(Math.Abs(v.X) - 1.0) < 1e-6) ones++;
            else if (Math.Abs(v.X) < 1e-6) zeros++;
            if (Math.Abs(Math.Abs(v.Y) - 1.0) < 1e-6) ones++;
            else if (Math.Abs(v.Y) < 1e-6) zeros++;
            if (Math.Abs(Math.Abs(v.Z) - 1.0) < 1e-6) ones++;
            else if (Math.Abs(v.Z) < 1e-6) zeros++;
            return ones == 1 && zeros == 2;
        }

        if (isAxisAligned(dirX) && isAxisAligned(dirY) && isAxisAligned(dirZ))
        {
            var pts = new List<Point3D>
            {
                corner,
                corner + dirX * width,
                corner + dirY * length,
                corner + dirX * width + dirY * length,
                corner + dirZ * height,
                corner + dirX * width + dirZ * height,
                corner + dirY * length + dirZ * height,
                corner + dirX * width + dirY * length + dirZ * height
            };

            double minX = pts.Min(p => p.X);
            double minY = pts.Min(p => p.Y);
            double minZ = pts.Min(p => p.Z);
            double maxX = pts.Max(p => p.X);
            double maxY = pts.Max(p => p.Y);
            double maxZ = pts.Max(p => p.Z);

            corner = new Point3D(minX, minY, minZ);
            width = maxX - minX;
            length = maxY - minY;
            height = maxZ - minZ;
            dirX = Vector3D.UnitX;
            dirY = Vector3D.UnitY;
            dirZ = Vector3D.UnitZ;
        }
        else 
        {
            if (height < 0) { corner = corner + dirZ * height; height = -height; }
            if (width < 0) { corner = corner + dirX * width; width = -width; }
            if (length < 0) { corner = corner + dirY * length; length = -length; }
        }

        corner = CleanP(corner);
        dirX = CleanV(dirX);
        dirY = CleanV(dirY);
        dirZ = CleanV(dirZ);
        height = Clean(height);
        width = Clean(width);
        length = Clean(length);
    }
}
