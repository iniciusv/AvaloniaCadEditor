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
var placaSuporteYA = Add(placaSuporteCarrinhoYPeca, at: Placement.At(centroXTrilhoA - 12.5, -12.5, alturaTopoCarrinhosY + 5.5, rotationZDegrees: 180), name: "placa_suporte_carrinho_y_a", color: "#0078D7");
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
// --- Hotend V6, pendurado sob o carrinho X ---
var alturaHotend = hotendPeca.AlturaBico + hotendPeca.AlturaBloco + hotendPeca.AlturaPescoco + hotendPeca.AlturaHeatsink;
var hotend = Add(hotendPeca, at: Placement.At(0, ladoExtrusoraY * 23, alturaTopoCarrinhosY - alturaHotend + 10, rotationZDegrees: rotacaoExtrusoraZ), name: "hotend_v6");
// --- Bloco impresso para fixação ao carrinho MGN9C ---
// Simétrico em Y (alojamentos de porca frontal e traseiro), então não muda com a inversão.
var blocoFixacao = Add(blocoFixacaoPeca, at: Placement.At(0, 0, alturaTopoCarrinhosY + carrinhoXPeca.Altura), name: "bloco_fixacao_mgn9", color: "#00CC6A");
// --- Suporte impresso do Hotend V6 ---
var suporteHotend = Add(suporteHotendPeca, at: Placement.At(0, ladoExtrusoraY * blocoFixacaoPeca.Comprimento / 2, alturaTopoCarrinhosY + carrinhoXPeca.Altura, rotationZDegrees: rotacaoExtrusoraZ), name: "suporte_hotend_v6", color: "#00CC6A");
// --- Extrusões de alumínio 2020: estrutura de apoio dos trilhos ---
// Mais longas que os trilhos Y (300 mm) — os trilhos ficam centralizados nelas.
var extrusaoSobTrilhoYPeca = new Extrusao2020(Comprimento: 300);
// Comprimento suficiente só até encostar nos carrinhos Y, sem avançar sobre eles.
var extrusaoSobTrilhoXPeca = new Extrusao2020(Comprimento: espacamentoTrilhosY - carrinhoYPeca.Largura);
// Centrada sob o trilho Y correspondente: mesmo X central (largura diferente); em Y, centralizada
// no meio do comprimento do trilho (que é menor que o da extrusão), só descendo em Z.
var folgaComprimentoY = (extrusaoSobTrilhoYPeca.Comprimento - trilhoYPeca.Comprimento) / 2;
var extrusaoSobTrilhoYA = Add(extrusaoSobTrilhoYPeca, at: Placement.At(trilhoYAX + trilhoYPeca.Largura / 2 - extrusaoSobTrilhoYPeca.Largura / 2, trilhoYAY - folgaComprimentoY, -extrusaoSobTrilhoYPeca.Altura), name: "extrusao_2020_sob_trilho_y_a");
var extrusaoSobTrilhoYB = Add(extrusaoSobTrilhoYPeca, at: Placement.At(trilhoYBX + trilhoYPeca.Largura / 2 - extrusaoSobTrilhoYPeca.Largura / 2, trilhoYBY - folgaComprimentoY, -extrusaoSobTrilhoYPeca.Altura), name: "extrusao_2020_sob_trilho_y_b");
var extrusaoSobTrilhoX = Add(extrusaoSobTrilhoXPeca with { Comprimento = 208 }, at: Placement.At(-extrusaoSobTrilhoXPeca.Comprimento / 2 - 11, extrusaoSobTrilhoXPeca.Largura / 2, alturaTopoCarrinhosY - extrusaoSobTrilhoXPeca.Altura, rotationZDegrees: -90), name: "extrusao_2020_sob_trilho_x");
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
var suporteMotorNema17 = Add(suporteMotorNema17Peca with { LarguraPorcaLateral = 5.83 }, at: Placement.At(xMotorNema17, yMotorSuporte, zSuporteMotor, rotationZDegrees: rotacaoMotorZ), name: "suporte_motor_nema17", color: "#00CC6A");
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
// --- Bloco de pressao da correia (belt), fixado na lateral do bloco_fixacao_mgn9 ---
// Acompanha o lado do motor: a correia vem da polia e o rebaixo inferior do bloco existe
// justamente para passar livre do bloco_conexao_motor_2020. A peça já traz o rebaixo cilíndrico
// do topo (furo_vertical) para alojar a cabeça dos parafusos M3 dos dois furos verticais.
var blocoFixacaoBeltPeca = new BlocoFixacaoBelt();
var blocoFixacaoBelt = Add(blocoFixacaoBeltPeca, at: Placement.At(0, 0, alturaTopoCarrinhosY + carrinhoXPeca.Altura, rotationZDegrees: rotacaoMotorZ), name: "bloco_fixacao_belt", color: "#00CC6A");
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
// --- Bloco 15 × 48 × 25 mm conectado à placa do carrinho Y pelos furos de fixação ---
// Assenta no topo da placa e seus dois furos passantes coincidem com os furo_fixacao_carrinho_1
// e furo_fixacao_carrinho_2 (25 mm de passo em Y, 3,5 mm da face externa da placa): o mesmo
// parafuso atravessa o bloco, a placa e vai à porca do carrinho MGN15H.
// A posição sai das PRÓPRIAS coordenadas locais dos furos da placa convertidas para o mundo
// (a placa B é espelhada em X, por isso o fator sx), então o bloco acompanha a placa se ela
// for movida/girada. O bloco é simétrico, logo não precisa de rotação para alinhar os furos.
var blocoConexaoPlacaCarrinhoYPeca = new BlocoConexaoPlacaCarrinhoY();
const double passoFurosPlacaCarrinhoY = 25.0;
var sxPlacaSuporteYB = placaSuporteYB.Part.EspelhadoNoEixoY ? -1.0 : 1.0;
var furoFixacaoPlacaB1Mundo = placaSuporteYB.ToWorld(new Point3D(passoFurosPlacaCarrinhoY / 2 * sxPlacaSuporteYB, -passoFurosPlacaCarrinhoY / 2, 0));
var furoFixacaoPlacaB2Mundo = placaSuporteYB.ToWorld(new Point3D(passoFurosPlacaCarrinhoY / 2 * sxPlacaSuporteYB, passoFurosPlacaCarrinhoY / 2, 0));
var topoPlacaSuporteYB = placaSuporteYB.ToWorld(new Point3D(0, 0, 5.5));
var blocoConexaoPlacaCarrinhoYB = Add(blocoConexaoPlacaCarrinhoYPeca, at: Placement.At((furoFixacaoPlacaB1Mundo.X + furoFixacaoPlacaB2Mundo.X) / 2.0, (furoFixacaoPlacaB1Mundo.Y + furoFixacaoPlacaB2Mundo.Y) / 2.0, topoPlacaSuporteYB.Z), name: "bloco_conexao_placa_carrinho_y_b", color: "#00CC6A");