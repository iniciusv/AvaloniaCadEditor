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
var carrinhoYA = Add(carrinhoYPeca, at: Placement.At(centroXTrilhoA - carrinhoYPeca.Largura / 2 - 12.5, -carrinhoYPeca.Comprimento / 2 + 12.5, 0), name: "carrinho_mgn15h_a");
var carrinhoYB = Add(carrinhoYPeca, at: Placement.At(centroXTrilhoB - carrinhoYPeca.Largura / 2 + 12.5, -carrinhoYPeca.Comprimento / 2 - 12.5, 0), name: "carrinho_mgn15h_b");
// --- Trilho X: MGN9 (250 mm), girado 90° para correr no sentido X, apoiado no topo dos carrinhos Y ---
var alturaTopoCarrinhosY = carrinhoYPeca.Altura;
var trilhoX = Add(trilhoXPeca, at: Placement.At(-trilhoXPeca.Comprimento / 2, trilhoXPeca.Largura / 2, alturaTopoCarrinhosY, rotationZDegrees: -90), name: "trilho_mgn9_x");
var carrinhoX = Add(carrinhoXPeca, at: Placement.At(-carrinhoXPeca.Comprimento / 2, carrinhoXPeca.Largura / 2, alturaTopoCarrinhosY, rotationZDegrees: -90), name: "carrinho_mgn9c_x");
// --- Hotend V6, pendurado sob o carrinho X ---
var alturaHotend = hotendPeca.AlturaBico + hotendPeca.AlturaBloco + hotendPeca.AlturaPescoco + hotendPeca.AlturaHeatsink;
var hotend = Add(hotendPeca, at: Placement.At(0, -23, alturaTopoCarrinhosY - alturaHotend + 10), name: "hotend_v6");
// --- Bloco impresso para fixação ao carrinho MGN9C ---
var blocoFixacao = Add(blocoFixacaoPeca, at: Placement.At(0, 0, alturaTopoCarrinhosY + carrinhoXPeca.Altura), name: "bloco_fixacao_mgn9", color: "#00CC6A");
// --- Suporte impresso do Hotend V6 ---
var suporteHotend = Add(suporteHotendPeca, at: Placement.At(0, -blocoFixacaoPeca.Comprimento / 2, alturaTopoCarrinhosY + carrinhoXPeca.Altura), name: "suporte_hotend_v6", color: "#00CC6A");
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
var extrusaoSobTrilhoX = Add(extrusaoSobTrilhoXPeca, at: Placement.At(-extrusaoSobTrilhoXPeca.Comprimento / 2, extrusaoSobTrilhoXPeca.Largura / 2, alturaTopoCarrinhosY - extrusaoSobTrilhoXPeca.Altura, rotationZDegrees: -90), name: "extrusao_2020_sob_trilho_x");
// --- Motor NEMA17, montado mais baixo, ao lado do carrinho X, apoiado em cima da extrusão 2020 ---
var topoExtrusaoSobTrilhoX = alturaTopoCarrinhosY;
var xMotorNema17 = carrinhoXPeca.Comprimento / 2 + suporteMotorNema17Peca.Largura / 2 + 5;
// com o blocoConexaoMotorPeca de 8 mm de profundidade encaixado entre a extrusão e o suporte;
// a placa avança para fora dela (lado livre), de modo que o motor (pendurado por baixo da placa)
// fique ao lado da extrusão, não sobre ela.
var faceLivreYExtrusaoSobTrilhoX = extrusaoSobTrilhoXPeca.Largura / 2;
var yBlocoConexao = faceLivreYExtrusaoSobTrilhoX + blocoConexaoMotorPeca.Profundidade / 2;
var zBlocoConexao = topoExtrusaoSobTrilhoX - extrusaoSobTrilhoXPeca.Altura;
var blocoConexaoMotor = Add(blocoConexaoMotorPeca, at: Placement.At(xMotorNema17, yBlocoConexao, zBlocoConexao), name: "bloco_conexao_motor_2020", color: "#00CC6A");
var yMotorSuporte = faceLivreYExtrusaoSobTrilhoX + blocoConexaoMotorPeca.Profundidade + suporteMotorNema17Peca.ComprimentoFrente;
var zSuporteMotor = zBlocoConexao + blocoConexaoMotorPeca.AlturaFurosSuporteZ - suporteMotorNema17Peca.Espessura / 2.0;
var suporteMotorNema17 = Add(suporteMotorNema17Peca with { LarguraPorcaLateral = 5.83 }, at: Placement.At(xMotorNema17, yMotorSuporte, zSuporteMotor), name: "suporte_motor_nema17", color: "#00CC6A");
// Motor voltado para cima (eixo em +Z, orientação normal da peça), mas pendurado por baixo do
// suporte: os furos de fixação do motor ficam na face de cima (lado do eixo/boss), então é essa
// face que precisa encostar na base do suporte — o corpo do motor fica por baixo, pendurado, e o
// eixo sobe através do furo de alívio central, se projetando por cima do suporte.
var alturaMontagemMotor = zSuporteMotor - motorNema17Peca.Comprimento;
var motorNema17 = Add(motorNema17Peca, at: Placement.At(xMotorNema17, yMotorSuporte, alturaMontagemMotor), name: "motor_nema17");
// --- Bloco de pressao da correia (belt), fixado na lateral do bloco_fixacao_mgn9 ---
var blocoFixacaoBelt = Add(new BlocoFixacaoBelt(), at: Placement.At(0, 0, alturaTopoCarrinhosY + carrinhoXPeca.Altura), name: "bloco_fixacao_belt", color: "#00CC6A");