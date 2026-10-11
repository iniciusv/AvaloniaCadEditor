using System;
using AvaloniaCadEditor.Domain.Features;

namespace AvaloniaCadEditor.Domain.ValueObjects;

public class FaceReference
{
    public IFeature ParentFeature { get; }
    public string Name { get; set; }
    public int FaceId { get; }
    public Vector3D NormalHint { get; }

    /// <summary>
    /// Um ponto da face (FaceId -1, face genérica de um sólido qualquer). Com ele a face é o PLANO
    /// (<see cref="NormalHint"/> + este ponto): um chanfro só corta arestas que ficam nele. Sem ele, só
    /// a normal identifica a face, e qualquer face paralela (ex.: o fundo de um rebaixo) também entra.
    /// </summary>
    public Point3D? PointOnFace { get; }

    public FaceReference(IFeature parent, int faceId, Vector3D normalHint, string name)
    {
        ParentFeature = parent;
        FaceId = faceId;
        NormalHint = normalHint;
        Name = name;
    }

    public FaceReference(IFeature parent, int faceId, Vector3D normalHint, Point3D pointOnFace, string name)
        : this(parent, faceId, normalHint, name)
    {
        PointOnFace = pointOnFace;
    }

    public WorkPlane GetWorkPlane()
    {
        if (ParentFeature is BoxFeature box)
        {
            if (Enum.IsDefined(typeof(FaceType), FaceId))
            {
                FaceType ft = (FaceType)FaceId;
                Point3D origin = box.GetFaceCenter(ft);
                Vector3D normal = box.GetFaceNormal(ft);
                Vector3D xAxis = box.GetFaceDirectionX(ft);

                return new WorkPlane(origin, normal, xAxis);
            }
        }
        else if (ParentFeature is CylinderFeature cyl)
        {
            if (Enum.IsDefined(typeof(FaceType), FaceId))
            {
                FaceType ft = (FaceType)FaceId;
                Point3D origin = cyl.GetFaceCenter(ft);
                Vector3D normal = cyl.GetFaceNormal(ft);

                // Fallback XAxis for cylinder top/bottom
                Vector3D xAxis = new Vector3D(1, 0, 0);
                if (Math.Abs(Math.Abs(normal.Z) - 1.0) < 1e-6)
                    xAxis = new Vector3D(1, 0, 0);
                else
                    xAxis = new Vector3D(0, 0, 1).CrossProduct(normal).Normalize();

                return new WorkPlane(origin, normal, xAxis);
            }
        }

        return new WorkPlane(NormalHint, PointOnFace ?? new Point3D(0, 0, 0));
    }
}
