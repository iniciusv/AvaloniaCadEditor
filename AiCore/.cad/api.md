# API do AvaloniaCad (gerada automaticamente — não edite)

Tipos e membros disponíveis no código das cenas. Gerado a partir do código do AvaloniaCad.

```
Propriedades comuns a TODAS as features (herdadas de FeatureBase):
  string Name { get; set; }
  bool IsEnabled { get; set; }
  bool IsVisible { get; set; }
  bool IsSelected { get; set; }
  bool IsTransientTool { get; set; }
  string Color { get; set; }

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

### ConeFeature : FeatureBase
  new ConeFeature(Point3D globalBaseCenter, Vector3D globalDirection, double bottomRadius, double topRadius, double height, string? name = null)
  Point3D GlobalBaseCenter { get; set; }
  Vector3D GlobalDirection { get; set; }
  double BottomRadius { get; set; }
  double TopRadius { get; set; }
  double Height { get; set; }
  bool IsFullCone { get; }
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

### SweepFeature : FeatureBase
  new SweepFeature(SweepSection section, SweepPath path, Vector3D? upReference = null, string? name = null)
  new SweepFeature(IProfile section, Vector3D bendDirection, double bendRadius, double angleDegrees, string? name = null)
  new SweepFeature(Point3D start, Vector3D startTangent, double sectionRadius, Vector3D bendDirection, double bendRadius, double angleDegrees, string? name = null)
  SweepSection Section { get; set; }
  SweepPath Path { get; set; }
  WorkPlane StartPlane { get; set; }
  Point3D StartCenter { get; }
  Vector3D StartTangent { get; }
  Point3D EndCenter { get; }
  Vector3D EndTangent { get; }
  IReadOnlyList<WorkPlane> GetStations()
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
  bool IsRotationAboutZOnly { get; }
  double RotationZRadians { get; }
  bool IsIdentity { get; }
  Placement Then(Placement next)
  Placement Inverse()
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
  Point3D ToLocal(Point3D world)
  Vector3D ToLocal(Vector3D worldDirection)
  IReadOnlyList<Point3D> GetVertices()

### SweepSection
  static SweepSection SweepSection.Circle(double radius)
  static SweepSection SweepSection.Rectangle(double width, double height)
  static SweepSection SweepSection.Polygon(IEnumerable<ValueTuple<double, double>> points)
  static SweepSection SweepSection.FromProfile(IProfile profile)
  IProfile Outline { get; }
  IReadOnlyList<ValueTuple<double, double>> GetPoints(int segments)

### SweepPath
  static SweepPath SweepPath.Arc(Point3D start, Vector3D startTangent, Vector3D bendDirection, double bendRadius, double angleDegrees)
  static SweepPath SweepPath.FullCircle(Point3D center, Vector3D axis, double radius)
  static double SweepPath.MaxCornerRadius(IEnumerable<Point3D> points, bool closed = false)
  static SweepPath SweepPath.FromPolyline(IEnumerable<Point3D> points, double cornerRadius, bool closed = false)
  IReadOnlyList<Point3D> Points { get; }
  double CornerRadius { get; }
  Point3D Start { get; }
  bool IsClosed { get; }
  IReadOnlyList<SweepSegment> Segments { get; }
  string Error { get; }
  Vector3D StartTangent { get; }
  IReadOnlyList<WorkPlane> Stations(WorkPlane startPlane)

enum FaceType { Top, Bottom, Front, Back, Left, Right }
enum EdgeType { TopFront, TopBack, TopLeft, TopRight, BottomFront, BottomBack, BottomLeft, BottomRight, FrontLeft, FrontRight, BackLeft, BackRight }
enum BooleanOperationType { Union, Subtract, Intersect }
enum FilletMode { Round, Bevel }
enum BoxCorner { BottomLeftFront, BottomRightFront, BottomRightBack, BottomLeftBack, TopLeftFront, TopRightFront, TopRightBack, TopLeftBack }
Métodos globais disponíveis no código da cena:
  void AddFeature(IFeature feature) — registra a feature na cena (só no arquivo de entrada).
  PartInstance<T> Add<T>(T part, Placement? at = null, string? name = null, string? color = null) where T : Part
    — coloca uma instância de peça na cena (só no arquivo de entrada). color é "#RRGGBB" e pinta o sólido inteiro.
  PartInstance<T> part.Instance<T>(string name, Placement? at = null) where T : Part
    — peça usada como operando de BooleanFeature dentro do Build() de outra peça (não entra na cena).
```

## Como escrever o código da cena

### Qualquer arquivo
- Unidades em milímetros. O eixo Z aponta para cima.
- NÃO use Console, System.IO, rede, threads, reflexão ou qualquer tipo de I/O.
- Todo número decimal usa ponto ('.'), nunca vírgula.
- Nomes de variáveis são únicos, curtos e em snake_case (ex.: box_a1, hole_b2, fillet_c3).
- SÓLIDOS RASTREÁVEIS POR NOME: variáveis, const, arrays, laços e funções locais podem ser usados
  para CALCULAR valores (posições, diâmetros, passos), mas cada sólido (BoxFeature, CylinderFeature,
  ConeFeature, PrismFeature, SweepFeature, ExtrudeFeature, FilletFeature, BooleanFeature...) deve ser criado numa
  declaração própria, com Name LITERAL igual ao nome da variável e único no arquivo:
  var furo_1 = new CylinderFeature(...) { Name = "furo_1" };
- NUNCA crie sólidos dentro de laços, LINQ (Select(... new CylinderFeature ...)) ou listas montadas
  dinamicamente, e NUNCA use nome interpolado ou concatenado ($"furo_{i}", "furo_" + i). Para N
  furos iguais, calcule as posições em variáveis e declare furo_1 ... furo_N, um por linha.

### Arquivo de entrada (Cena.cs) — statements top-level
- Contém APENAS declarações e chamadas soltas (statements top-level). NUNCA use 'using',
  'namespace', 'class', 'struct', 'interface', 'record' nem declare métodos.
- Toda feature deve ter Name = "<mesmo nome da variável>" e ser registrada com AddFeature(variavel).
- Ferramentas booleanas transitórias (ex.: o cilindro usado apenas para furar) devem ter
  IsTransientTool = true e também devem ser registradas com AddFeature.
- Para usar uma peça JÁ DECLARADA em Pecas/, NÃO redeclare o tipo aqui: chame
  var p = Add(new NomeDaPeca(...), at: Placement.At(x, y, z));
- NUNCA declare uma peça/objeto reutilizável NOVO neste arquivo. Um objeto ou sólido reutilizável
  (motor, parafuso, suporte etc.) É uma peça: ele vai num arquivo próprio em Pecas/, mesmo que
  apareça só uma vez na cena.

### Peças (Pecas/*.cs)
- Um arquivo de peça contém EXATAMENTE UM
  'public sealed record NomeDaPeca(<parâmetros com valor padrão>) : Part { ... }'.
- NUNCA use statements soltos, 'using', nem chame AddFeature/Add num arquivo de peça — isso é só
  do arquivo de entrada da cena.
- 'protected override IFeature Build()' constrói a geometria em coordenadas LOCAIS (origem da peça
  em 0,0,0) e devolve UMA ÚNICA IFeature; não chama AddFeature/Add.
- Para peças compostas (corpo + furos + eixo etc.), monte tudo DENTRO do próprio Build() com
  BooleanFeature.CreateUnion/CreateDifference/CreateIntersection e devolva o resultado final.
- Um conjunto de sólidos que se repete (ex.: furo + alojamento de porca) é uma peça própria em
  Pecas/, usada como operando dentro do Build() de outra com .Instance("nome") — sem Add, e com
  o nome literal igual ao da variável:
  var fixacao_1 = new FuroPorca(new Point3D(17, 4, 1), new Vector3D(-1, 0, 0), 6).Instance("fixacao_1");
  (opcional: .Instance("nome", at: Placement...) para posicionar/girar a sub-peça).
- Dê a cada parâmetro um nome em português e um valor padrão sensato (para que 'new NomeDaPeca()'
  já funcione sozinho); pode usar [System.ComponentModel.DataAnnotations.Range(min, max)] (nome
  completo, sem using) — valores fora do intervalo viram erro.
- Em Pecas/ também pode haver arquivos AUXILIARES (ex.: uma static class Medidas com constantes):
  só declarações de tipos, sem statements soltos.

### Instanciando uma peça no arquivo de entrada
- Instanciar na cena: var p1 = Add(new Placa(100, 60), at: Placement.At(0, 80, 0, rotationZDegrees: 90));
- Variantes de uma peça: expressão 'with' do C# sobre p1.Part —
  var p2 = Add(p1.Part with { Espessura = 8 }, at: Placement.At(150, 0, 0));
- Ponto local da peça convertido para o mundo: p2.World(p => new Point3D(p.Largura, 0, 0)).
- color ("#RRGGBB") é opcional e pinta o sólido inteiro; só use se o pedido envolver cores.
- Peças com os mesmos parâmetros compartilham a geometria (construída uma vez); cada instância só
  acrescenta a sua posição.

### Exemplos de peça

```csharp
public sealed record Placa(double Largura = 100, double Comprimento = 60, double Espessura = 5) : Part
{
    protected override IFeature Build() =>
        new BoxFeature(Point3D.Origin, Largura, Comprimento, Espessura) { Name = "placa" };
}
```

```csharp
public sealed record ChapaFurada(double Largura = 50, double Comprimento = 30, double Espessura = 5, double DiametroFuro = 5) : Part
{
    protected override IFeature Build()
    {
        var corpo = new BoxFeature(Point3D.Origin, Largura, Comprimento, Espessura) { Name = "corpo" };
        var furo_1 = new CylinderFeature(new Point3D(DiametroFuro, DiametroFuro, 0), Vector3D.UnitZ, DiametroFuro / 2, Espessura) { Name = "furo_1", IsTransientTool = true };
        var furo_2 = new CylinderFeature(new Point3D(Largura - DiametroFuro, Comprimento - DiametroFuro, 0), Vector3D.UnitZ, DiametroFuro / 2, Espessura) { Name = "furo_2", IsTransientTool = true };
        return BooleanFeature.CreateDifference("chapa_furada", corpo, furo_1, furo_2);
    }
}
```

```csharp
public sealed record BlocoQuatroFuros(double Largura = 20, double Comprimento = 30, double Altura = 10) : Part
{
    protected override IFeature Build()
    {
        const double passoX = 15;
        const double passoY = 10;
        const double diametroFuro = 3;
        const double profundidadeFuro = 3;

        var x1 = Largura / 2 - passoX / 2;
        var x2 = Largura / 2 + passoX / 2;
        var y1 = Comprimento / 2 - passoY / 2;
        var y2 = Comprimento / 2 + passoY / 2;
        var zFuro = Altura - profundidadeFuro;

        var corpo = new BoxFeature(Point3D.Origin, Largura, Comprimento, Altura) { Name = "corpo" };
        var furo_1 = new CylinderFeature(new Point3D(x1, y1, zFuro), Vector3D.UnitZ, diametroFuro / 2, profundidadeFuro) { Name = "furo_1", IsTransientTool = true };
        var furo_2 = new CylinderFeature(new Point3D(x2, y1, zFuro), Vector3D.UnitZ, diametroFuro / 2, profundidadeFuro) { Name = "furo_2", IsTransientTool = true };
        var furo_3 = new CylinderFeature(new Point3D(x1, y2, zFuro), Vector3D.UnitZ, diametroFuro / 2, profundidadeFuro) { Name = "furo_3", IsTransientTool = true };
        var furo_4 = new CylinderFeature(new Point3D(x2, y2, zFuro), Vector3D.UnitZ, diametroFuro / 2, profundidadeFuro) { Name = "furo_4", IsTransientTool = true };
        return BooleanFeature.CreateDifference("bloco_quatro_furos", corpo, furo_1, furo_2, furo_3, furo_4);
    }
}
```

E o arquivo de entrada que usa a `Placa` acima (sem redeclarar o tipo):

```csharp
var placa_1 = Add(new Placa(100, 60));
var placa_2 = Add(new Placa(100, 60), at: Placement.At(0, 80, 0));
```
