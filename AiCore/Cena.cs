var trilhoYPeca = new TrilhoMgn15(Comprimento: 200);
var carrinhoYPeca = new CarrinhoMgn15H();
var trilhoXPeca = new TrilhoMgn9(Comprimento: 250);
var carrinhoXPeca = new CarrinhoMgn9C();
var hotendPeca = new HotendV6();
var blocoFixacaoPeca = new BlocoFixacaoMgn9(Altura: 10);
var suporteHotendPeca = new SuporteHotendV6(Largura: 50, DistanciaYHotend: 23.0 - blocoFixacaoPeca.Comprimento / 2);
var motorNema17Peca = new MotorNema17();
var suporteMotorNema17Peca = new SuporteMotorNema17(Espessura: 10);
var blocoConexaoMotorPeca = new BlocoConexaoMotor2020();
var placaSuporteCarrinhoYPeca = new PlacaSuporteCarrinhoY();
// Espaçamento (centro a centro) entre os dois trilhos Y: igual à distância entre os dois últimos
// furos do trilho X — é por eles que o trilho MGN9 é parafusado diretamente nos carrinhos Y.
var espacamentoTrilhosY = trilhoXPeca.DistanciaEntreUltimosFuros;
var centroXTrilhoA = -espacamentoTrilhosY / 2;
var centroXTrilhoB = espacamentoTrilhosY / 2;
// --- Trilhos Y: 2x MGN15, correndo no sentido Y (sem rotação), lado a lado no X ---
// Posição (canto) de cada trilho Y; ajustada manualmente no editor — mantida em variáveis
// para que as extrusões 2020 de apoio (abaixo) sigam o mesmo alinhamento.
var trilhoYAX = centroXTrilhoA - trilhoYPeca.Largura / 2 - 12.276393;
var trilhoYAY = -trilhoYPeca.Comprimento / 2 - 12.947214;
var trilhoYBX = centroXTrilhoB - trilhoYPeca.Largura / 2 + 12.5;
var trilhoYBY = -trilhoYPeca.Comprimento / 2 - 12.5;
var trilhoYA = Add(trilhoYPeca, at: Placement.At(trilhoYAX, trilhoYAY, 0), name: "trilho_mgn15_y_a");
var trilhoYB = Add(trilhoYPeca, at: Placement.At(trilhoYBX, trilhoYBY, 0), name: "trilho_mgn15_y_b");
// Os dois carrinhos Y compartilham a mesma coordenada Y (a do carrinho B).
var carrinhoYY = -carrinhoYPeca.Comprimento / 2 - 12.5;
var carrinhoYA = Add(carrinhoYPeca, at: Placement.At(centroXTrilhoA - carrinhoYPeca.Largura / 2 - 12.5, carrinhoYY, 0), name: "carrinho_mgn15h_a");
var carrinhoYB = Add(carrinhoYPeca, at: Placement.At(centroXTrilhoB - carrinhoYPeca.Largura / 2 + 12.5, carrinhoYY, 0), name: "carrinho_mgn15h_b");
// --- Trilho X: MGN9 (250 mm), girado 90° para correr no sentido X, apoiado no topo dos carrinhos Y ---
var alturaTopoCarrinhosY = carrinhoYPeca.Altura;
// --- Placas de suporte impressas, uma parafusada no topo de cada carrinho MGN15H ---
// Base da placa (Z local = -5,5) encosta no topo do carrinho (Z = 16); os 3 furos passantes
// alinham com a malha 25 × 25 dos furos M3 do topo do carrinho. As duas peças são giradas 180°
// e seus centros ficam deslocados meia-malha (12,5 mm) em X e Y, de modo que os 3 furos
// coincidam com 3 dos 4 furos M3 do carrinho (o 4º canto é o recorte da placa).
// A placa B é espelhada no eixo Y (EspelhadoNoEixoY), tornando-se a imagem especular da placa A.
var placaSuporteYA = Add(placaSuporteCarrinhoYPeca, at: Placement.At(centroXTrilhoA - 12.5, -12.5, alturaTopoCarrinhosY + 5.5, rotationZDegrees: 180), name: "placa_suporte_carrinho_y_a", color: "#00CC6A");
var placaSuporteYB = Add(placaSuporteCarrinhoYPeca with { EspelhadoNoEixoY = true }, at: Placement.At(centroXTrilhoB + 12.5, -12.5, alturaTopoCarrinhosY + 5.5, rotationZDegrees: 180), name: "placa_suporte_carrinho_y_b", color: "#00CC6A");
var trilhoX = Add(trilhoXPeca, at: Placement.At(-trilhoXPeca.Comprimento / 2, trilhoXPeca.Largura / 2, alturaTopoCarrinhosY, rotationZDegrees: -90), name: "trilho_mgn9_x");
var carrinhoX = Add(carrinhoXPeca, at: Placement.At(-carrinhoXPeca.Comprimento / 2, carrinhoXPeca.Largura / 2, alturaTopoCarrinhosY, rotationZDegrees: -90), name: "carrinho_mgn9c_x");
// --- Lados da extrusora e do motor em relação ao trilho X (plano Y = 0) ---
// Os dois subconjuntos ficam em lados opostos do trilho. Trocar os dois sinais abaixo inverte
// um pelo outro; a rotação de 180° em Z acompanha o sinal, porque as peças são simétricas em X
// e precisam continuar voltadas para o trilho depois de mudar de lado.
const double ladoExtrusoraY = 1; // extrusora em +Y
const double ladoMotorY = -1; // motor (e bloco da correia) em -Y
var rotacaoExtrusoraZ = ladoExtrusoraY > 0 ? 180.0 : 0.0;
var rotacaoMotorZ = ladoMotorY > 0 ? 0.0 : 180.0;
// --- Conexão bloco → suporte do hotend ---
// O suporte do hotend fica afastado do bloco de fixação pela espessura da conexão (em Y, para o lado
// da extrusora) e desnivelado para baixo; hotend e suporte acompanham as duas medidas.
var conexaoBlocoSuporteHotendPeca = new ConexaoBlocoSuporteHotend(Largura: blocoFixacaoPeca.Largura, Espessura: 8, Desnivel: 20, AlturaBloco: blocoFixacaoPeca.Altura, AlturaSuporte: suporteHotendPeca.Altura);
// --- Hotend V6, pendurado sob o carrinho X ---
var alturaHotend = hotendPeca.AlturaBico + hotendPeca.AlturaBloco + hotendPeca.AlturaPescoco + hotendPeca.AlturaHeatsink;
var hotend = Add(hotendPeca, at: Placement.At(0, ladoExtrusoraY * (23 + conexaoBlocoSuporteHotendPeca.Espessura), alturaTopoCarrinhosY - alturaHotend + 10 - conexaoBlocoSuporteHotendPeca.Desnivel, rotationZDegrees: rotacaoExtrusoraZ), name: "hotend_v6");
// --- Bloco impresso para fixação ao carrinho MGN9C ---
// Simétrico em Y (alojamentos de porca frontal e traseiro), então não muda com a inversão.
var blocoFixacao = Add(blocoFixacaoPeca, at: Placement.At(0, 0, alturaTopoCarrinhosY + carrinhoXPeca.Altura), name: "bloco_fixacao_mgn9", color: "#00CC6A");
// --- Suporte impresso do Hotend V6 ---
var suporteHotend = Add(suporteHotendPeca, at: Placement.At(0, ladoExtrusoraY * (blocoFixacaoPeca.Comprimento / 2 + conexaoBlocoSuporteHotendPeca.Espessura), alturaTopoCarrinhosY + carrinhoXPeca.Altura - conexaoBlocoSuporteHotendPeca.Desnivel, rotationZDegrees: rotacaoExtrusoraZ), name: "suporte_hotend_v6", color: "#00CC6A");
// A conexão encosta na face do bloco do lado da extrusora, com a base rente à do suporte; girada
// como o suporte, o −Y local (para o suporte) aponta para o lado da extrusora.
var conexaoBlocoSuporteHotend = Add(conexaoBlocoSuporteHotendPeca, at: Placement.At(0, ladoExtrusoraY * blocoFixacaoPeca.Comprimento / 2, alturaTopoCarrinhosY + carrinhoXPeca.Altura - conexaoBlocoSuporteHotendPeca.Desnivel, rotationZDegrees: rotacaoExtrusoraZ), name: "conexao_bloco_suporte_hotend", color: "#00CC6A");
// --- Extrusões de alumínio 2020: estrutura de apoio dos trilhos ---
// Extrusões sob os trilhos Y: 400 mm, crescendo só para +Y a partir da ponta -Y (que continua
// onde estava, junto ao carrinho Z e ao tubo travessa). Os trilhos ficam na metade -Y delas.
var extrusaoSobTrilhoYPeca = new Extrusao2020(Comprimento: 400);
// Comprimento suficiente só até encostar nos carrinhos Y, sem avançar sobre eles.
var extrusaoSobTrilhoXPeca = new Extrusao2020(Comprimento: espacamentoTrilhosY - carrinhoYPeca.Largura);
// Centradas sob o trilho Y correspondente em X (larguras diferentes); em Y, a ponta -Y fica um
// pouco antes do início do trilho e o comprimento total cresce apenas para +Y.
const double folgaExtrusaoYAntesDoTrilho = 50;
var extrusaoSobTrilhoYA = Add(extrusaoSobTrilhoYPeca, at: Placement.At(trilhoYAX + trilhoYPeca.Largura / 2 - extrusaoSobTrilhoYPeca.Largura / 2, trilhoYAY - folgaExtrusaoYAntesDoTrilho, -extrusaoSobTrilhoYPeca.Altura), name: "extrusao_2020_sob_trilho_y_a");
var extrusaoSobTrilhoYB = Add(extrusaoSobTrilhoYPeca, at: Placement.At(trilhoYBX + trilhoYPeca.Largura / 2 - extrusaoSobTrilhoYPeca.Largura / 2, trilhoYBY - folgaExtrusaoYAntesDoTrilho, -extrusaoSobTrilhoYPeca.Altura), name: "extrusao_2020_sob_trilho_y_b");
var extrusaoSobTrilhoX = Add(extrusaoSobTrilhoXPeca with { Comprimento = 208 }, at: Placement.At(-extrusaoSobTrilhoXPeca.Comprimento / 2 - 11, extrusaoSobTrilhoXPeca.Largura / 2, alturaTopoCarrinhosY - extrusaoSobTrilhoXPeca.Altura, rotationZDegrees: -90), name: "extrusao_2020_sob_trilho_x");
// --- Eixo Z: 2x trilho HGR15 vertical, cada um com o seu carrinho HGH15 na ponta -Y de uma extrusão Y ---
// Os carrinhos são a parte móvel: levam o gantry inteiro (extrusões, trilhos Y e tudo o que está
// neles) para cima e para baixo; os trilhos ficam presos na estrutura da máquina. Cada carrinho fica
// na lateral externa (em X) da sua extrusão, alinhado à ponta -Y dela, com o topo (face de fixação)
// voltado para o perfil e o trilho para fora.
// As peças que prendem carrinho e extrusão ainda são modeladas fora do software; a espessura delas
// entra em espessuraFixacaoCarrinhoZ (0 = carrinho encostado direto no perfil, até elas virem para cá).
var trilhoZPeca = new TrilhoHgr15(Comprimento: 300);
var carrinhoZPeca = new CarrinhoHgh15();
const double espessuraFixacaoCarrinhoZ = 0;
// Ponta de cima do carrinho 10 mm abaixo da base das extrusões Y; a posição dele no trilho é a
// altura do gantry.
const double folgaCarrinhoZAbaixoExtrusao = 10;
var zBaseExtrusoesY = -extrusaoSobTrilhoYPeca.Altura;
var zCentroCarrinhoZ = zBaseExtrusoesY - folgaCarrinhoZAbaixoExtrusao - carrinhoZPeca.Comprimento / 2;
var cantoExtrusaoYA = extrusaoSobTrilhoYA.ToWorld(new Point3D(0, 0, 0));
var cantoExtrusaoYB = extrusaoSobTrilhoYB.ToWorld(new Point3D(extrusaoSobTrilhoYPeca.Largura, 0, 0));
// Carrinho deitado na lateral: o comprimento (Y local) vira Z do mundo e o topo (Z local) aponta
// para a extrusão — +X do mundo no lado A (giro de +90° em Z), −X no lado B (giro de −90°). A
// largura (X local) vai da ponta -Y do perfil para +Y nos dois lados.
var deitarCarrinhoZ = Placement.Rotation(Vector3D.UnitX, 90);
var carrinhoZA = Add(carrinhoZPeca, at: deitarCarrinhoZ.Then(Placement.At(cantoExtrusaoYA.X - espessuraFixacaoCarrinhoZ - carrinhoZPeca.Altura, cantoExtrusaoYA.Y, zCentroCarrinhoZ - carrinhoZPeca.Comprimento / 2, rotationZDegrees: 90)), name: "carrinho_hgh15_z_a");
var carrinhoZB = Add(carrinhoZPeca, at: deitarCarrinhoZ.Then(Placement.At(cantoExtrusaoYB.X + espessuraFixacaoCarrinhoZ + carrinhoZPeca.Altura, cantoExtrusaoYB.Y + carrinhoZPeca.Largura, zCentroCarrinhoZ - carrinhoZPeca.Comprimento / 2, rotationZDegrees: -90)), name: "carrinho_hgh15_z_b");
// Trilho no canal do carrinho e centrado nele (meio do curso): a origem do trilho é o ponto local
// (RecuoTrilho, meio do carrinho − meio do trilho, 0) do carrinho, com a mesma orientação.
var origemTrilhoNoCarrinhoZ = new Point3D(carrinhoZPeca.RecuoTrilho, (carrinhoZPeca.Comprimento - trilhoZPeca.Comprimento) / 2, 0);
var trilhoZA = Add(trilhoZPeca, at: deitarCarrinhoZ.Then(Placement.At(carrinhoZA.ToWorld(origemTrilhoNoCarrinhoZ), rotationZDegrees: 90)), name: "trilho_hgr15_z_a");
var trilhoZB = Add(trilhoZPeca, at: deitarCarrinhoZ.Then(Placement.At(carrinhoZB.ToWorld(origemTrilhoNoCarrinhoZ), rotationZDegrees: -90)), name: "trilho_hgr15_z_b");
// --- Extrusões 2020 verticais (400 mm) onde cada trilho Z é parafusado ---
// A base do trilho encosta numa face do perfil, do lado de fora em X; o perfil fica centrado no
// trilho em Y e em Z. Deitar o perfil (giro de 90° em X) leva o comprimento (Y local) para Z do
// mundo, o X local continua X e o Z local vai para −Y — daí o +Largura/2 em Y. A seção é simétrica,
// então os dois lados usam o mesmo giro.
var extrusaoTrilhoZPeca = new Extrusao2020(Comprimento: 400);
var centroBaseTrilhoZ = new Point3D(trilhoZPeca.Largura / 2, trilhoZPeca.Comprimento / 2, 0);
var centroBaseTrilhoZA = trilhoZA.ToWorld(centroBaseTrilhoZ);
var centroBaseTrilhoZB = trilhoZB.ToWorld(centroBaseTrilhoZ);
var deitarExtrusaoTrilhoZ = Placement.Rotation(Vector3D.UnitX, 90);
var extrusaoTrilhoZA = Add(extrusaoTrilhoZPeca, at: deitarExtrusaoTrilhoZ.Then(Placement.At(centroBaseTrilhoZA.X - extrusaoTrilhoZPeca.Largura, centroBaseTrilhoZA.Y + extrusaoTrilhoZPeca.Altura / 2, centroBaseTrilhoZA.Z - extrusaoTrilhoZPeca.Comprimento / 2)), name: "extrusao_2020_trilho_z_a");
var extrusao_2020_trilho_z_a_copia1 = Add(extrusaoTrilhoZPeca, at: deitarExtrusaoTrilhoZ.Then(Placement.At(centroBaseTrilhoZA.X - extrusaoTrilhoZPeca.Largura, centroBaseTrilhoZA.Y + extrusaoTrilhoZPeca.Altura / 2, centroBaseTrilhoZA.Z - extrusaoTrilhoZPeca.Comprimento / 2)).Then(Placement.RotationAbout(new Point3D(180.499969, -155.5, -260.700012), Vector3D.UnitX, -90)), name: "extrusao_2020_trilho_z_a_copia1");
var extrusaoTrilhoZB = Add(extrusaoTrilhoZPeca, at: deitarExtrusaoTrilhoZ.Then(Placement.At(centroBaseTrilhoZB.X, centroBaseTrilhoZB.Y + extrusaoTrilhoZPeca.Altura / 2, centroBaseTrilhoZB.Z - extrusaoTrilhoZPeca.Comprimento / 2)), name: "extrusao_2020_trilho_z_b");
var extrusao_2020_trilho_z_b_copia1 = Add(extrusaoTrilhoZPeca, at: deitarExtrusaoTrilhoZ.Then(Placement.At(centroBaseTrilhoZB.X, centroBaseTrilhoZB.Y + extrusaoTrilhoZPeca.Altura / 2, centroBaseTrilhoZB.Z - extrusaoTrilhoZPeca.Comprimento / 2)).Then(Placement.RotationAbout(new Point3D(180.499969, -155.5, -260.700012), Vector3D.UnitX, -90)), name: "extrusao_2020_trilho_z_b_copia1");
// --- Cantoneira impressa que amarra a placa do carrinho Y ao perfil 2020 transversal ---
// Origem na interseção de três planos da montagem: face interna da placa A (a placa é girada
// 180°, logo sua face local x = -16 vira +16), face lateral -Y do perfil (é para esse lado que
// apontam os furos com porca da placa) e meia-altura do perfil, que é a altura do rasgo T.
// O bloco_conexao_motor_2020 usa a mesma face, mas no centro do perfil (X de -20 a 20), longe
// da faixa desta cantoneira.
// A do lado B é a imagem especular da do lado A (cresce em -X a partir da face daquela placa).
var cantoneiraPlacaExtrusaoPeca = new CantoneiraPlacaExtrusao2020();
var xFaceInternaPlacaA = centroXTrilhoA - 12.5 + 16;
var xFaceInternaPlacaB = centroXTrilhoB + 12.5 - 16;
var yFaceFrontalExtrusaoX = -extrusaoSobTrilhoXPeca.Largura / 2;
var zMeioExtrusaoX = alturaTopoCarrinhosY - extrusaoSobTrilhoXPeca.Altura / 2;
var cantoneiraPlacaExtrusaoA = Add(cantoneiraPlacaExtrusaoPeca, at: Placement.At(xFaceInternaPlacaA, yFaceFrontalExtrusaoX, zMeioExtrusaoX), name: "cantoneira_placa_extrusao_a", color: "#00CC6A");
var cantoneiraPlacaExtrusaoB = Add(cantoneiraPlacaExtrusaoPeca with { EspelhadoNoEixoX = true }, at: Placement.At(xFaceInternaPlacaB, yFaceFrontalExtrusaoX, zMeioExtrusaoX), name: "cantoneira_placa_extrusao_b", color: "#00CC6A");
// --- Motor NEMA17, montado mais baixo, ao lado do carrinho X, apoiado em cima da extrusão 2020 ---
var topoExtrusaoSobTrilhoX = alturaTopoCarrinhosY;
// Centralizado no perfil de alumínio 2020 (extrusao_2020_sob_trilho_x), cujo centro em X é 0.
var xMotorNema17 = 0;
// com o blocoConexaoMotorPeca de 8 mm de profundidade encaixado entre a extrusão e o suporte;
// a placa avança para fora dela (lado do motor), de modo que o motor (pendurado por baixo da
// placa) fique ao lado da extrusão, não sobre ela.
var faceLateralYExtrusaoSobTrilhoX = extrusaoSobTrilhoXPeca.Largura / 2;
var yBlocoConexao = ladoMotorY * (faceLateralYExtrusaoSobTrilhoX + blocoConexaoMotorPeca.Profundidade / 2);
var zBlocoConexao = topoExtrusaoSobTrilhoX - extrusaoSobTrilhoXPeca.Altura;
var blocoConexaoMotor = Add(blocoConexaoMotorPeca, at: Placement.At(xMotorNema17, yBlocoConexao, zBlocoConexao, rotationZDegrees: rotacaoMotorZ), name: "bloco_conexao_motor_2020", color: "#00CC6A");
var yMotorSuporte = ladoMotorY * (faceLateralYExtrusaoSobTrilhoX + blocoConexaoMotorPeca.Profundidade + suporteMotorNema17Peca.ComprimentoFrente);
var zSuporteMotor = zBlocoConexao + blocoConexaoMotorPeca.AlturaFurosSuporteZ - suporteMotorNema17Peca.Espessura / 2.0;
var suporteMotorNema17 = Add(suporteMotorNema17Peca, at: Placement.At(xMotorNema17, yMotorSuporte, zSuporteMotor, rotationZDegrees: rotacaoMotorZ), name: "suporte_motor_nema17", color: "#00CC6A");
// Motor voltado para cima (eixo em +Z, orientação normal da peça), mas pendurado por baixo do
// suporte: os furos de fixação do motor ficam na face de cima (lado do eixo/boss), então é essa
// face que precisa encostar na base do suporte — o corpo do motor fica por baixo, pendurado, e o
// eixo sobe através do furo de alívio central, se projetando por cima do suporte.
var alturaMontagemMotor = zSuporteMotor - motorNema17Peca.Comprimento;
var motorNema17 = Add(motorNema17Peca, at: Placement.At(xMotorNema17, yMotorSuporte, alturaMontagemMotor, rotationZDegrees: rotacaoMotorZ), name: "motor_nema17");
// --- Espaçador cilíndrico de 15 mm, apoiado no topo do suporte e alinhado ao furo_motor_1 ---
// O furo passante do espaçador tem o MESMO diâmetro dos furos de fixação do motor (mesmo vazio)
// e fica exatamente sobre o furo_motor_1 do suporte; o corpo do cilindro é deslocado em relação
// ao furo (furo fora de centro), crescendo na diagonal para o centro da placa, para o cilindro
// inteiro caber sobre ela. O furo do furo_motor_1, em coordenadas locais do suporte, é o canto
// -X/-Y do padrão de furos do motor.
var cilindroEspacadorMotorPeca = new CilindroEspacadorMotor(Altura: 15, DiametroFuro: suporteMotorNema17Peca.DiametroFuroMotor);
var furoMotor1Local = new Point3D(-suporteMotorNema17Peca.PassoFurosMotor / 2, -suporteMotorNema17Peca.PassoFurosMotor / 2, 0);
var furoMotor1Mundo = suporteMotorNema17.ToWorld(furoMotor1Local);
// O deslocamento do furo é no +X local da peça; girando 225° ele aponta para -X/-Y do mundo,
// ou seja, do furo para o centro da placa (o suporte é girado 180°, então o canto fica em +X/+Y).
var rotacaoEspacadorMotorZ = 225.0;
var deslocamentoEspacadorMotor = Placement.RotationZ(rotacaoEspacadorMotorZ).ApplyVector(new Vector3D(cilindroEspacadorMotorPeca.DeslocamentoFuro, 0, 0));
var zTopoSuporteMotor = zSuporteMotor + suporteMotorNema17Peca.Espessura;
var cilindroEspacadorMotor = Add(cilindroEspacadorMotorPeca with { DiametroExterno = 5.9 }, at: Placement.At(furoMotor1Mundo.X - deslocamentoEspacadorMotor.X, furoMotor1Mundo.Y - deslocamentoEspacadorMotor.Y, zTopoSuporteMotor, rotationZDegrees: rotacaoEspacadorMotorZ), name: "cilindro_espacador_motor", color: "#00CC6A");
// --- Segundo espaçador, no furo_motor_2: a imagem do primeiro pelo plano X do motor ---
// Os dois ficam nos furos do lado do trilho X e desviam os dois ramos da correia que saem da polia
// para os rolamentos presos no porca_inferior_2 das placas de suporte A e B. O furo_motor_2 é o
// canto +X/−Y local do suporte, que com o giro de 180° cai em −X/+Y do mundo; por isso o corpo
// cresce para +X/−Y em relação ao furo (giro de 315°), espelhando os 225° do primeiro.
var furoMotor2Local = new Point3D(suporteMotorNema17Peca.PassoFurosMotor / 2, -suporteMotorNema17Peca.PassoFurosMotor / 2, 0);
var furoMotor2Mundo = suporteMotorNema17.ToWorld(furoMotor2Local);
var rotacaoEspacadorMotor2Z = 315.0;
var deslocamentoEspacadorMotor2 = Placement.RotationZ(rotacaoEspacadorMotor2Z).ApplyVector(new Vector3D(cilindroEspacadorMotor.Part.DeslocamentoFuro, 0, 0));
var cilindroEspacadorMotor2 = Add(cilindroEspacadorMotor.Part, at: Placement.At(furoMotor2Mundo.X - deslocamentoEspacadorMotor2.X, furoMotor2Mundo.Y - deslocamentoEspacadorMotor2.Y, zTopoSuporteMotor, rotationZDegrees: rotacaoEspacadorMotor2Z), name: "cilindro_espacador_motor_2", color: "#00CC6A");
// --- Bloco de pressao da correia (belt), fixado na lateral do bloco_fixacao_mgn9 ---
// Acompanha o lado do motor: a correia vem da polia e o rebaixo inferior do bloco existe
// justamente para passar livre do bloco_conexao_motor_2020. A peça já traz o rebaixo cilíndrico
// do topo (furo_vertical) para alojar a cabeça dos parafusos M3 dos dois furos verticais.
var blocoFixacaoBeltPeca = new BlocoFixacaoBelt();
var blocoFixacaoBelt = Add(blocoFixacaoBeltPeca, at: Placement.At(0, 0, alturaTopoCarrinhosY + carrinhoXPeca.Altura, rotationZDegrees: rotacaoMotorZ), name: "bloco_fixacao_belt", color: "#00CC6A");
// --- Altura da correia do eixo X ---
// A correia de 6 mm corre apoiada no fundo do vão da garra do bloco_fixacao_belt: é o carrinho que
// fixa essa altura, e a polia e os rolamentos se alinham a ela.
const double larguraCorreiaX = 6;
var zFundoVaoGarraBelt = blocoFixacaoBelt.ToWorld(new Point3D(0, 0, blocoFixacaoBeltPeca.AlturaCorpo - blocoFixacaoBeltPeca.AlturaCorreia)).Z;
var zCentroCorreiaX = zFundoVaoGarraBelt + larguraCorreiaX / 2;
// --- Polia GT2 de 24 dentes do eixo X: 10 mm, sem cubo (correia de 6 mm) ---
// No eixo do motor, com a faixa dentada centrada na correia (fica 4 mm acima do topo do suporte),
// na mesma faixa de altura dos dois espaçadores que desviam a correia.
var poliaXPeca = new PoliaGt2(Dentes: 24, Altura: 10);
var zBasePoliaX = zCentroCorreiaX - (poliaXPeca.AlturaCubo + poliaXPeca.EspessuraFlange + poliaXPeca.LarguraDentes / 2);
var poliaX = Add(poliaXPeca, at: Placement.At(xMotorNema17, yMotorSuporte, zBasePoliaX), name: "polia_gt2_x");
// --- Rolamentos da correia do eixo X ---
// Os rolamentos que desviam a correia ficam centrados nela, sempre aos pares costas com costas
// (flanges para fora), para a correia de 6 mm correr entre as duas flanges:
//  - nas placas de suporte A e B, no furo porca_inferior_2: arruela do topo da placa até dois
//    F623ZZ (8 mm, 6 mm entre flanges). O M3 desce por eles e pela placa até a porca alojada na
//    face inferior;
//  - nos dois espaçadores do suporte do motor: dois F686ZZ (10 mm, 7,8 mm entre flanges) sobre o
//    corpo Ø5,9 do espaçador, apoiados numa arruela no topo do suporte — um só deixava 3,9 mm
//    acima da flange. Como o furo do espaçador é excêntrico, girá-lo pelo sextavado desloca os
//    rolamentos e tensiona a correia.
var rolamentoF623Peca = Rolamento.F623ZZ;
var zBaseRolamentosPlaca = zCentroCorreiaX - rolamentoF623Peca.Largura;
// porca_inferior_2 fica em (−4,5·sx, 10) local da placa; z = 5,5 é o topo dela.
var topoPorcaInferior2PlacaA = placaSuporteYA.ToWorld(new Point3D(-4.5 * (placaSuporteYA.Part.EspelhadoNoEixoY ? -1.0 : 1.0), 10, 5.5));
var topoPorcaInferior2PlacaB = placaSuporteYB.ToWorld(new Point3D(-4.5 * (placaSuporteYB.Part.EspelhadoNoEixoY ? -1.0 : 1.0), 10, 5.5));
var arruelaRolamentoPlacaPeca = new ArruelaEspacadora(Altura: zBaseRolamentosPlaca - topoPorcaInferior2PlacaA.Z);
var arruelaRolamentoPlacaA = Add(arruelaRolamentoPlacaPeca, at: Placement.At(topoPorcaInferior2PlacaA), name: "arruela_rolamento_placa_a", color: "#00CC6A");
var arruelaRolamentoPlacaB = Add(arruelaRolamentoPlacaPeca, at: Placement.At(topoPorcaInferior2PlacaB), name: "arruela_rolamento_placa_b", color: "#00CC6A");
var rolamentoPlacaAInferior = Add(rolamentoF623Peca, at: Placement.At(topoPorcaInferior2PlacaA.X, topoPorcaInferior2PlacaA.Y, zBaseRolamentosPlaca), name: "rolamento_f623_placa_a_inferior");
var rolamentoPlacaASuperior = Add(rolamentoF623Peca with { FlangeNoTopo = true }, at: Placement.At(topoPorcaInferior2PlacaA.X, topoPorcaInferior2PlacaA.Y, zBaseRolamentosPlaca + rolamentoF623Peca.Largura), name: "rolamento_f623_placa_a_superior");
var rolamentoPlacaBInferior = Add(rolamentoF623Peca, at: Placement.At(topoPorcaInferior2PlacaB.X, topoPorcaInferior2PlacaB.Y, zBaseRolamentosPlaca), name: "rolamento_f623_placa_b_inferior");
var rolamentoPlacaBSuperior = Add(rolamentoF623Peca with { FlangeNoTopo = true }, at: Placement.At(topoPorcaInferior2PlacaB.X, topoPorcaInferior2PlacaB.Y, zBaseRolamentosPlaca + rolamentoF623Peca.Largura), name: "rolamento_f623_placa_b_superior");
// Rolamento do espaçador: coaxial com o CORPO do espaçador (a origem dele), não com o furo.
var rolamentoF686Peca = Rolamento.F686ZZ;
var zBaseRolamentosEspacador = zCentroCorreiaX - rolamentoF686Peca.Largura;
var arruelaRolamentoEspacadorPeca = new ArruelaEspacadora(Altura: zBaseRolamentosEspacador - zTopoSuporteMotor, DiametroFuro: cilindroEspacadorMotor.Part.DiametroExterno + 0.3, DiametroExterno: 10, DiametroColar: 7.5);
var centroEspacadorMotor1 = cilindroEspacadorMotor.ToWorld(Point3D.Origin);
var centroEspacadorMotor2 = cilindroEspacadorMotor2.ToWorld(Point3D.Origin);
var arruelaRolamentoEspacador1 = Add(arruelaRolamentoEspacadorPeca, at: Placement.At(centroEspacadorMotor1), name: "arruela_rolamento_espacador_1", color: "#00CC6A");
var arruelaRolamentoEspacador2 = Add(arruelaRolamentoEspacadorPeca, at: Placement.At(centroEspacadorMotor2), name: "arruela_rolamento_espacador_2", color: "#00CC6A");
var rolamentoEspacador1Inferior = Add(rolamentoF686Peca, at: Placement.At(centroEspacadorMotor1.X, centroEspacadorMotor1.Y, zBaseRolamentosEspacador), name: "rolamento_f686_espacador_1_inferior");
var rolamentoEspacador1Superior = Add(rolamentoF686Peca with { FlangeNoTopo = true }, at: Placement.At(centroEspacadorMotor1.X, centroEspacadorMotor1.Y, zBaseRolamentosEspacador + rolamentoF686Peca.Largura), name: "rolamento_f686_espacador_1_superior");
var rolamentoEspacador2Inferior = Add(rolamentoF686Peca, at: Placement.At(centroEspacadorMotor2.X, centroEspacadorMotor2.Y, zBaseRolamentosEspacador), name: "rolamento_f686_espacador_2_inferior");
var rolamentoEspacador2Superior = Add(rolamentoF686Peca with { FlangeNoTopo = true }, at: Placement.At(centroEspacadorMotor2.X, centroEspacadorMotor2.Y, zBaseRolamentosEspacador + rolamentoF686Peca.Largura), name: "rolamento_f686_espacador_2_superior");
// --- Correia GT2 do eixo X ---
// Tira lisa (sem os dentes) varrida pela linha de centro da correia, no plano zCentroCorreiaX, de
// uma ponta presa na garra do bloco_fixacao_belt até a outra:
//   garra (−X) → rolamentos da placa A → espaçador 2 → polia → espaçador 1 → rolamentos da placa B → garra (+X)
// Espaçadores e polia formam um ômega: a correia abraça a polia pelos dentes e passa pelas costas
// nos rolamentos dos espaçadores. Em cada garra a correia entra pelo canal encostada na parede, dá
// a volta no parafuso, contorna o nariz e a ponta volta pelo canal como segunda camada. A espessura
// de 1,2 mm faz as duas camadas caberem no canal de 2,45 mm.
// Os sentidos das voltas (vistos de +Z) e o lado da garra de cada ponta valem para o motor em −Y
// (garra girada 180°: a metade +X local dela fica do lado da placa A).
const double espessuraCorreiaX = 1.2;
var garraBelt = blocoFixacaoBeltPeca.Garra;
Point3D NoPlanoCorreiaX(Point3D p) => new Point3D(p.X, p.Y, zCentroCorreiaX);
Point3D NaGarraBelt(double x, double y) => NoPlanoCorreiaX(blocoFixacaoBelt.ToWorld(blocoFixacaoBeltPeca.PosicaoGarra.Apply(new Point3D(x, y, 0))));
// Raios da linha de centro: o de cada peça mais meia espessura da correia.
var raioCorreiaNarizGarra = garraBelt.RaioNariz + espessuraCorreiaX / 2;
var raioCorreiaParafusoGarra = garraBelt.RaioParafuso + espessuraCorreiaX / 2;
var raioCorreiaF623 = rolamentoF623Peca.DiametroExterno / 2 + espessuraCorreiaX / 2;
var raioCorreiaF686 = rolamentoF686Peca.DiametroExterno / 2 + espessuraCorreiaX / 2;
var raioCorreiaPoliaX = poliaXPeca.DiametroExterno / 2 + espessuraCorreiaX / 2;
// Pontas na boca do canal, na camada de cima (encostada no teto do canal, tangente ao nariz).
var pontaCorreiaXA = NaGarraBelt(garraBelt.Comprimento / 2, garraBelt.NarizY - raioCorreiaNarizGarra);
var pontaCorreiaXB = NaGarraBelt(-garraBelt.Comprimento / 2, garraBelt.NarizY - raioCorreiaNarizGarra);
var caminhoCorreiaX = SweepPath.AroundCircles(pontaCorreiaXA, new[]
{
    new SweepWrap(NaGarraBelt(-garraBelt.NarizX, garraBelt.NarizY), raioCorreiaNarizGarra, Clockwise: true),
    new SweepWrap(NaGarraBelt(garraBelt.DistanciaParafuso, garraBelt.ParafusoY), raioCorreiaParafusoGarra, Clockwise: false),
    new SweepWrap(NoPlanoCorreiaX(topoPorcaInferior2PlacaA), raioCorreiaF623, Clockwise: false),
    new SweepWrap(NoPlanoCorreiaX(centroEspacadorMotor2), raioCorreiaF686, Clockwise: true),
    new SweepWrap(NoPlanoCorreiaX(poliaX.ToWorld(Point3D.Origin)), raioCorreiaPoliaX, Clockwise: false),
    new SweepWrap(NoPlanoCorreiaX(centroEspacadorMotor1), raioCorreiaF686, Clockwise: true),
    new SweepWrap(NoPlanoCorreiaX(topoPorcaInferior2PlacaB), raioCorreiaF623, Clockwise: false),
    new SweepWrap(NaGarraBelt(-garraBelt.DistanciaParafuso, garraBelt.ParafusoY), raioCorreiaParafusoGarra, Clockwise: false),
    new SweepWrap(NaGarraBelt(garraBelt.NarizX, garraBelt.NarizY), raioCorreiaNarizGarra, Clockwise: true),
}, pontaCorreiaXB, Vector3D.UnitZ);
var correiaGt2X = new SweepFeature(SweepSection.Rectangle(espessuraCorreiaX, larguraCorreiaX), caminhoCorreiaX, upReference: Vector3D.UnitZ) { Name = "correia_gt2_x", Color = "#303030" };
AddFeature(correiaGt2X);
// --- Chapa de 20 x 10 mm (arestas chanfradas) assentada no topo do bloco_fixacao_belt ---
// Reproduz, na mesma posição, os dois furos verticais de 3,2 mm do bloco (furo_vertical_3_2 e
// o seu espelhado, distantes 11 mm). As posições são obtidas convertendo as coordenadas locais
// desses furos para o mundo, então a chapa acompanha o bloco mesmo se ele for girado/movido.
var chapaFurosBeltPeca = new ChapaFurosBelt();
var xPlanoEspelhoBelt = blocoFixacaoBeltPeca.BaseCorpoX + blocoFixacaoBeltPeca.LarguraCorpoX;
var furoVerticalBeltA = blocoFixacaoBelt.ToWorld(new Point3D(blocoFixacaoBeltPeca.FuroVerticalX, blocoFixacaoBeltPeca.FuroVerticalY, 0));
var furoVerticalBeltB = blocoFixacaoBelt.ToWorld(new Point3D(2.0 * xPlanoEspelhoBelt - blocoFixacaoBeltPeca.FuroVerticalX, blocoFixacaoBeltPeca.FuroVerticalY, 0));
var xChapaFurosBelt = (furoVerticalBeltA.X + furoVerticalBeltB.X) / 2.0 - chapaFurosBeltPeca.LarguraX / 2.0;
var yChapaFurosBelt = (furoVerticalBeltA.Y + furoVerticalBeltB.Y) / 2.0 - chapaFurosBeltPeca.ComprimentoY / 2.0;
var zChapaFurosBelt = alturaTopoCarrinhosY + carrinhoXPeca.Altura + blocoFixacaoBeltPeca.AlturaCorpo;
var chapaFurosBelt = Add(chapaFurosBeltPeca, at: Placement.At(xChapaFurosBelt, yChapaFurosBelt, zChapaFurosBelt), name: "chapa_furos_belt", color: "#00CC6A");
// Referências da placa A para posicionar a conexão da correia (a placa B é a espelhada, por isso sx).
var sxPlacaSuporteYA = placaSuporteYA.Part.EspelhadoNoEixoY ? -1.0 : 1.0;
var topoPlacaSuporteYA = placaSuporteYA.ToWorld(new Point3D(0, 0, 5.5));
// --- Conexão da correia à placa A: garra de correia + parede + fixação superior ---
// A face de apoio da peça (y = 0 local) encosta na face externa da placa A e o topo da garra fica
// rente ao topo da placa. O plano de simetria da garra (x = 0 local) fica no eixo da fixacao_x_6
// da placa (y = 2 local da placa), que atravessa a garra; os furos da fixação superior caem sobre
// furo_fixacao_carrinho_1 e furo_fixacao_carrinho_2. Girada 90° em Z: o +Y local (para fora da
// garra) aponta para −X do mundo, para longe da placa.
var blocoFicacaoBeltVaoCorreiaPeca = new BlocoFicacaoBeltVaoCorreia();
var xFaceExternaPlacaA = placaSuporteYA.ToWorld(new Point3D(16 * sxPlacaSuporteYA, 0, 0)).X;
var yFixacaoX6PlacaA = placaSuporteYA.ToWorld(new Point3D(16 * sxPlacaSuporteYA, 2, 0)).Y;
var zBlocoFicacaoBeltVaoCorreia = topoPlacaSuporteYA.Z - blocoFicacaoBeltVaoCorreiaPeca.Altura;
var blocoFicacaoBeltVaoCorreia = Add(blocoFicacaoBeltVaoCorreiaPeca, at: Placement.At(xFaceExternaPlacaA, yFixacaoX6PlacaA, zBlocoFicacaoBeltVaoCorreia, rotationZDegrees: 90), name: "bloco_ficacao_belt_vao_correia", color: "#00CC6A");
// --- Tubo retangular 50 x 30 (parede 2, quinas R2) apoiado sobre as pontas -Y das extrusões Y ---
// Vai da face externa da extrusão A à da extrusão B. Em Y parte da ponta mais recuada das duas (a
// da A; a da B fica 0,45 mm à frente) e avança 50 mm para +Y. Comprimento do tubo no Y local:
// girado −90° em Z, o Y local aponta para +X do mundo e o X local para −Y, por isso a origem fica
// no canto +Y.
var tuboTravessaPeca = new TuboRetangular(Comprimento: cantoExtrusaoYB.X - cantoExtrusaoYA.X, Largura: 50, Altura: 30);
var yPontaExtrusoesY = Math.Min(cantoExtrusaoYA.Y, cantoExtrusaoYB.Y);
var tuboTravessa = Add(tuboTravessaPeca, at: Placement.At(cantoExtrusaoYA.X, yPontaExtrusoesY + tuboTravessaPeca.Largura, 0, rotationZDegrees: -90), name: "tubo_travessa");
// --- Motores NEMA17 do eixo Y (movem carrinhoYA e carrinhoYB), deitados sobre o tubo travessa ---
// Vistos de frente (plano XZ), cada motor fica com a face do eixo voltada para fora, para o trilho Z
// do seu lado; a polia vai no eixo, no vão entre a ponta do tubo e o trilho Z, e a correia corre em Y.
// A ponta do eixo para folgaEixoMotorYTrilhoZ antes da face interna do trilho Z.
// Girar 90° em Y leva o eixo do motor (Z local) para X do mundo: −90° aponta para −X (lado A),
// +90° para +X (lado B). O X local vai para ±Z do mundo, então o corpo ocupa ±Lado/2 em Z em torno
// da origem: ela fica Lado/2 acima do topo do tubo para o motor apoiar nele.
const double folgaEixoMotorYTrilhoZ = 1;
var zTopoTuboTravessa = tuboTravessa.ToWorld(new Point3D(0, 0, tuboTravessaPeca.Altura)).Z;
var yCentroTuboTravessa = tuboTravessa.ToWorld(new Point3D(tuboTravessaPeca.Largura / 2, 0, 0)).Y;
var xFaceInternaTrilhoZA = trilhoZA.ToWorld(new Point3D(0, 0, trilhoZPeca.Altura)).X;
var xFaceInternaTrilhoZB = trilhoZB.ToWorld(new Point3D(0, 0, trilhoZPeca.Altura)).X;
// Da base traseira do motor (origem local) até a ponta do eixo.
var alcanceEixoMotorY = motorNema17Peca.Comprimento + motorNema17Peca.AlturaBoss + motorNema17Peca.ComprimentoEixo;
var zMotorY = zTopoTuboTravessa + motorNema17Peca.Lado / 2;
var motorYA = Add(motorNema17Peca, at: Placement.Rotation(Vector3D.UnitY, -90).Then(Placement.At(xFaceInternaTrilhoZA + folgaEixoMotorYTrilhoZ + alcanceEixoMotorY, yCentroTuboTravessa, zMotorY)), name: "motor_nema17_y_a");
var motorYB = Add(motorNema17Peca, at: Placement.Rotation(Vector3D.UnitY, 90).Then(Placement.At(xFaceInternaTrilhoZB - folgaEixoMotorYTrilhoZ - alcanceEixoMotorY, yCentroTuboTravessa, zMotorY)), name: "motor_nema17_y_b");
// --- Polias GT2 de 24 dentes do eixo Y: 20 mm, com cubo de 7 mm voltado para o motor (correia de 10 mm) ---
// No eixo de cada motor, folgaPoliaBossMotorY além do boss; giradas como o motor, o +Z da polia
// aponta para fora junto com o eixo.
const double folgaPoliaBossMotorY = 1;
var poliaYPeca = new PoliaGt2(Dentes: 24, Altura: 20, AlturaCubo: 7);
var basePoliaYNoMotor = new Point3D(0, 0, motorNema17Peca.Comprimento + motorNema17Peca.AlturaBoss + folgaPoliaBossMotorY);
var poliaYA = Add(poliaYPeca, at: Placement.Rotation(Vector3D.UnitY, -90).Then(Placement.At(motorYA.ToWorld(basePoliaYNoMotor))), name: "polia_gt2_y_a");
var poliaYB = Add(poliaYPeca, at: Placement.Rotation(Vector3D.UnitY, 90).Then(Placement.At(motorYB.ToWorld(basePoliaYNoMotor))), name: "polia_gt2_y_b");
