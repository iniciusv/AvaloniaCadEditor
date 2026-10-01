# API do AvaloniaCad (gerada automaticamente — não edite)

Tipos e membros disponíveis no código das cenas. Gerado a partir do código do AvaloniaCad.

```
Propriedades comuns a TODAS as features (herdadas de FeatureBase):
  string Name { get; set; }
  bool IsEnabled { get; set; }
  bool IsVisible { get; set; }
  bool IsSelected { get; set; }
  bool IsTransientTool { get; set; }

### BoxFeature : FeatureBase
  new BoxFeature(Point3D baseCorner, double width, double length, double height, string? name = null)
  Point3D BaseCorner { get; set; }
  double Width { get; set; }
  double Length { get; set; }
  double Height { get; set; }
  Vector3D DirectionX { get; set; }
  Vector3D DirectionY { get; set; }
  Vector3D DirectionZ { get; set; }
  Point3D GetFaceCenter(FaceType faceType)
  Point3D GetFaceCorner(FaceType faceType)
  Vector3D GetFaceNormal(FaceType faceType)
  double GetFaceWidth(FaceType faceType)
  double GetFaceLength(FaceType faceType)
  FaceType? GetFaceTypeFromNormal(Vector3D normal)
  void AlignToFace(BoxFeature parentBox, FaceType faceType)
  BoxFeature CreateExtrusion(FaceType faceType, double extrusionHeight, string? name = null)
  Vector3D GetFaceDirectionX(FaceType faceType)
  Vector3D GetFaceDirectionY(FaceType faceType)
  IReadOnlyList<Point3D> GetVertices()
  Point3D GetVertex(BoxCorner corner)
  EdgeType[] GetEdgesForFace(FaceType faceType)
  EdgeType[] GetAllEdges()
  ValueTuple<Point3D, Point3D, Vector3D, Vector3D> GetEdgeGeometry(EdgeType edgeType)

### CylinderFeature : FeatureBase
  new CylinderFeature(Point3D globalBaseCenter, Vector3D globalDirection, double radius, double height, string? name = null)
  static CylinderFeature CylinderFeature.FromWorkPlane(WorkPlane plane, Point3D localCenter, double radius, double height, string? name = null)
  Point3D GlobalBaseCenter { get; set; }
  Vector3D GlobalDirection { get; set; }
  double Radius { get; set; }
  double Height { get; set; }
  Point3D GetFaceCenter(FaceType faceType)
  Vector3D GetFaceNormal(FaceType faceType)
  FaceType? GetFaceTypeFromNormal(Vector3D normal)
  IReadOnlyList<Point3D> GetVertices()

### PrismFeature : FeatureBase
  new PrismFeature(IEnumerable<Point3D> vertices, double height, WorkPlane? plane = null, string? name = null)
  IReadOnlyList<Point3D> Vertices { get; }
  double Height { get; set; }
  WorkPlane Plane { get; set; }
  IReadOnlyList<Point3D> GetVertices()

### BooleanFeature : FeatureBase
  new BooleanFeature(string name, BooleanOperationType operation, IFeature baseFeature, IEnumerable<IFeature> toolFeatures)
  new BooleanFeature(string name, BooleanOperationType operation, IEnumerable<IFeature> baseFeatures, IEnumerable<IFeature> toolFeatures)
  static BooleanFeature BooleanFeature.CreateUnion(string name, IFeature baseFeature, params IFeature[] toolFeatures)
  static BooleanFeature BooleanFeature.CreateUnion(string name, IEnumerable<IFeature> baseFeatures, params IFeature[] toolFeatures)
  static BooleanFeature BooleanFeature.CreateDifference(string name, IFeature baseFeature, params IFeature[] toolFeatures)
  static BooleanFeature BooleanFeature.CreateDifference(string name, IEnumerable<IFeature> baseFeatures, params IFeature[] toolFeatures)
  static BooleanFeature BooleanFeature.CreateIntersection(string name, IFeature baseFeature, params IFeature[] toolFeatures)
  static BooleanFeature BooleanFeature.CreateIntersection(string name, IEnumerable<IFeature> baseFeatures, params IFeature[] toolFeatures)
  BooleanOperationType Operation { get; set; }
  List<IFeature> BaseFeatures { get; set; }
  List<IFeature> ToolFeatures { get; set; }
  IFeature BaseFeature { get; }
  IEnumerable<IFeature> Dependencies { get; }
  void AddBaseFeature(IFeature feature)
  void RemoveBaseFeature(IFeature feature)
  void AddToolFeature(IFeature feature)
  void RemoveToolFeature(IFeature feature)
  IFeature ChangeBaseFeature(IFeature newBase)

### FilletFeature : FeatureBase
  new FilletFeature(string name, IFeature baseFeature, double radius)
  IFeature BaseFeature { get; set; }
  List<EdgeReference> Edges { get; set; }
  List<FaceReference> Faces { get; set; }
  double Radius { get; set; }
  FilletMode Mode { get; set; }
  bool ApplyToAllEdges { get; set; }
  IEnumerable<IFeature> Dependencies { get; }
  void AddEdge(EdgeReference edge)
  void AddFace(FaceReference face)

### WorkPlane
  new WorkPlane(Vector3D normal, Point3D origin)
  new WorkPlane(Point3D origin, Vector3D normal, Vector3D xAxis)
  Vector3D Normal { get; }
  Point3D Origin { get; }
  Vector3D XAxis { get; }
  Vector3D YAxis { get; }
  Point3D ToWorldSpace(Point3D localPoint)
  Point3D ToLocalSpace(Point3D worldPoint)
  bool Equals(WorkPlane other)
  bool Equals(Object obj)
  int GetHashCode()

### LinkedWorkPlane : WorkPlane
  new LinkedWorkPlane(FaceReference targetFace)
  FaceReference TargetFace { get; }
  Point3D Origin { get; }
  Vector3D Normal { get; }
  Vector3D XAxis { get; }
  Vector3D YAxis { get; }
  void MarkDirty()

### FaceReference
  new FaceReference(IFeature parent, int faceId, Vector3D normalHint, string name)
  IFeature ParentFeature { get; }
  string Name { get; set; }
  int FaceId { get; }
  Vector3D NormalHint { get; }
  WorkPlane GetWorkPlane()

### EdgeReference
  new EdgeReference(IFeature parent, EdgeType edgeId, string name = "")
  IFeature ParentFeature { get; }
  string Name { get; set; }
  EdgeType EdgeId { get; }
  bool Equals(Object obj)
  int GetHashCode()

### Point3D
  new Point3D(double x, double y, double z = 0)
  double X { get; }
  double Y { get; }
  double Z { get; }
  double DistanceTo(Point3D other)
  bool Equals(Point3D other)
  bool Equals(Object obj)
  int GetHashCode()
  string ToString()

### Vector3D
  new Vector3D(double x, double y, double z = 0)
  double X { get; }
  double Y { get; }
  double Z { get; }
  double Length { get; }
  Vector3D Normalize()
  double DotProduct(Vector3D other)
  Vector3D CrossProduct(Vector3D other)
  bool Equals(Vector3D other)
  bool Equals(Object obj)
  int GetHashCode()

### Placement
  static Placement Placement.At(double x, double y, double z = 0, double rotationZDegrees = 0)
  static Placement Placement.At(Point3D point, double rotationZDegrees = 0)
  static Placement Placement.Translate(Vector3D offset)
  static Placement Placement.RotationZ(double degrees)
  static Placement Placement.RotationAbout(Point3D pivot, Vector3D axis, double degrees)
  static Placement Placement.Rotation(Vector3D axis, double degrees)
  Vector3D Translation { get; }
  bool HasRotation { get; }
  bool IsIdentity { get; }
  Placement Then(Placement next)
  Point3D Apply(Point3D p)
  Vector3D ApplyVector(Vector3D v)
  void ApplyToFloats(Single[] values, int offset, bool isPoint)
  string ToString()

### PartInstance : FeatureBase
  Part Part { get; }
  IFeature Definition { get; }
  Placement Placement { get; }
  string PartTypeName { get; }
  Point3D ToWorld(Point3D local)
  Vector3D ToWorld(Vector3D localDirection)
  IReadOnlyList<Point3D> GetVertices()

enum FaceType { Top, Bottom, Front, Back, Left, Right }
enum EdgeType { TopFront, TopBack, TopLeft, TopRight, BottomFront, BottomBack, BottomLeft, BottomRight, FrontLeft, FrontRight, BackLeft, BackRight }
enum BooleanOperationType { Union, Subtract, Intersect }
enum FilletMode { Round, Bevel }
enum BoxCorner { BottomLeftFront, BottomRightFront, BottomRightBack, BottomLeftBack, TopLeftFront, TopRightFront, TopRightBack, TopLeftBack }
Método global disponível no script: void AddFeature(IFeature feature) — registra a feature na cena.

PEÇAS PARAMÉTRICAS (em projetos de cena com vários arquivos):
  Declare em Pecas/ um record que herda de Part; os parâmetros são o construtor primário. Cada arquivo em
  Pecas/ declara EXATAMENTE UM record desses — nunca statements soltos, nunca AddFeature/Add (isso é só
  do arquivo de entrada da cena, ex.: Cena.cs). Um objeto/sólido reutilizável (motor, parafuso, suporte,
  etc.) É uma peça: crie/edite o arquivo dele em Pecas/ neste formato, mesmo que ele só apareça uma vez na cena.
    public sealed record Placa(double Largura, double Comprimento, double Espessura = 5) : Part
    {
        protected override IFeature Build() =>
            new BoxFeature(Point3D.Origin, Largura, Comprimento, Espessura) { Name = "placa" };
    }
  Build() constrói a peça em coordenadas LOCAIS (origem em 0,0,0) e NÃO chama AddFeature/Add. Para peças
  compostas (corpo + furos + eixo, etc.), combine tudo DENTRO do próprio Build() com
  BooleanFeature.CreateUnion/CreateDifference/CreateIntersection e devolva a IFeature final — instâncias de
  peça (PartInstance) ainda não podem ser operandos de BooleanFeature, só as features criadas dentro do Build():
    public sealed record ChapaFurada(double Largura = 50, double Comprimento = 30, double Espessura = 5) : Part
    {
        protected override IFeature Build()
        {
            var corpo = new BoxFeature(Point3D.Origin, Largura, Comprimento, Espessura) { Name = "corpo" };
            var furo = new CylinderFeature(new Point3D(Largura / 2, Comprimento / 2, 0), Vector3D.UnitZ, 3, Espessura) { Name = "furo", IsTransientTool = true };
            return BooleanFeature.CreateDifference("chapa_furada", corpo, furo);
        }
    }
  Parâmetros podem ter [System.ComponentModel.DataAnnotations.Range(min, max)]; valores fora do intervalo viram erro.
  Método global: PartInstance<T> Add<T>(T part, Placement? at = null, string? name = null) — coloca uma instância na cena.
    var p1 = Add(new Placa(100, 60));
    var p2 = Add(p1.Part with { Espessura = 8 }, at: Placement.At(150, 0, 0, rotationZDegrees: 90));
    Point3D canto = p2.World(p => new Point3D(p.Largura, 0, 0)); // ponto local da peça convertido para o mundo
  Peças com os mesmos parâmetros compartilham a geometria (construída uma vez). Instâncias ainda não podem ser operandos de BooleanFeature.
```
