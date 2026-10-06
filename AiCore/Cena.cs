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
var placaSuporteYA = Add(placaSuporteCarrinhoYPeca,
    at: Placement.At(centroXTrilhoA - 12.5, -12.5, alturaTopoCarrinhosY + 5.5, rotationZDegrees: 180),
    name: "placa_suporte_carrinho_y_a", color: "#00CC6A");
var placaSuporteYB = Add(placaSuporteCarrinhoYPeca with { EspelhadoNoEixoY = true },
    at: Placement.At(centroXTrilhoB + 12.5, -12.5, alturaTopoCarrinhosY + 5.5, rotationZDegrees: 180),
    name: "placa_suporte_carrinho_y_b", color: "#00CC6A");
var trilhoX = Add(trilhoXPeca, at: Placement.At(-trilhoXPeca.Comprimento / 2, trilhoXPeca.Largura / 2, alturaTopoCarrinhosY, rotationZDegrees: -90), name: "trilho_mgn9_x");
var carrinhoX = Add(carrinhoXPeca, at: Placement.At(-carrinhoXPeca.Comprimento / 2, carrinhoXPeca.Largura / 2, alturaTopoCarrinhosY, rotationZDegrees: -90), name: "carrinho_mgn9c_x");
// --- Lados da extrusora e do motor em relação ao trilho X (plano Y = 0) ---
// Os dois subconjuntos ficam em lados opostos do trilho. Trocar os dois sinais abaixo inverte
// um pelo outro; a rotação de 180° em Z acompanha o sinal, porque as peças são simétricas em X
// e precisam continuar voltadas para o trilho depois de mudar de lado.
const double ladoExtrusoraY = 1;    // extrusora em +Y
const double ladoMotorY = -1;       // motor (e bloco da correia) em -Y
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
var cantoneiraPlacaExtrusaoA = Add(cantoneiraPlacaExtrusaoPeca,
    at: Placement.At(xFaceInternaPlacaA, yFaceFrontalExtrusaoX, zMeioExtrusaoX),
    name: "cantoneira_placa_extrusao_a", color: "#00CC6A");
var cantoneiraPlacaExtrusaoB = Add(cantoneiraPlacaExtrusaoPeca with { EspelhadoNoEixoX = true },
    at: Placement.At(xFaceInternaPlacaB, yFaceFrontalExtrusaoX, zMeioExtrusaoX),
    name: "cantoneira_placa_extrusao_b", color: "#00CC6A");
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
// --- Bloco de pressao da correia (belt), fixado na lateral do bloco_fixacao_mgn9 ---
// Acompanha o lado do motor: a correia vem da polia e o rebaixo inferior do bloco existe
// justamente para passar livre do bloco_conexao_motor_2020.
var blocoFixacaoBelt = Add(new BlocoFixacaoBelt(), at: Placement.At(0, 0, alturaTopoCarrinhosY + carrinhoXPeca.Altura, rotationZDegrees: rotacaoMotorZ), name: "bloco_fixacao_belt", color: "#00CC6A");
// --- Conexão em Y para mangueira de 5 mm ---
// Boca inferior apoiada no plano: a peça vai de -20 a +25 em Z local, então o z do Placement é 20.
var conexaoY = Add(new ConexaoYAdaptador5mm(ComprimentoRamal: 15), at: Placement.At(120, 120, 20), name: "conexao_y_5mm", color: "#C8A2C8");