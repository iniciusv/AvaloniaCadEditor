var trilhoYPeca = new TrilhoMgn15(Comprimento: 200);
var carrinhoYPeca = new CarrinhoMgn15H();
var trilhoXPeca = new TrilhoMgn9(Comprimento: 250);
var carrinhoXPeca = new CarrinhoMgn9C();
var hotendPeca = new HotendV6();

// Espaçamento (centro a centro) entre os dois trilhos Y: igual ao comprimento do trilho X,
// para que ele encoste nos dois carrinhos Y de ponta a ponta (ajuste se o pórtico real for diferente).
var espacamentoTrilhosY = trilhoXPeca.Comprimento;
var centroXTrilhoA = -espacamentoTrilhosY / 2;
var centroXTrilhoB = espacamentoTrilhosY / 2;

// --- Trilhos Y: 2x MGN15, correndo no sentido Y (sem rotação), lado a lado no X ---
var trilhoYA = Add(trilhoYPeca, at: Placement.At(centroXTrilhoA - trilhoYPeca.Largura / 2, -trilhoYPeca.Comprimento / 2, 0), name: "trilho_mgn15_y_a");
var trilhoYB = Add(trilhoYPeca, at: Placement.At(centroXTrilhoB - trilhoYPeca.Largura / 2, -trilhoYPeca.Comprimento / 2, 0), name: "trilho_mgn15_y_b");

var carrinhoYA = Add(carrinhoYPeca, at: Placement.At(centroXTrilhoA - carrinhoYPeca.Largura / 2, -carrinhoYPeca.Comprimento / 2, 0), name: "carrinho_mgn15h_a");
var carrinhoYB = Add(carrinhoYPeca, at: Placement.At(centroXTrilhoB - carrinhoYPeca.Largura / 2, -carrinhoYPeca.Comprimento / 2, 0), name: "carrinho_mgn15h_b");

// --- Trilho X: MGN9 (250 mm), girado 90° para correr no sentido X, apoiado no topo dos carrinhos Y ---
var alturaTopoCarrinhosY = carrinhoYPeca.Altura;

var trilhoX = Add(
    trilhoXPeca,
    at: Placement.At(-trilhoXPeca.Comprimento / 2, trilhoXPeca.Largura / 2, alturaTopoCarrinhosY, rotationZDegrees: -90),
    name: "trilho_mgn9_x");

var carrinhoX = Add(
    carrinhoXPeca,
    at: Placement.At(-carrinhoXPeca.Comprimento / 2, carrinhoXPeca.Largura / 2, alturaTopoCarrinhosY, rotationZDegrees: -90),
    name: "carrinho_mgn9c_x");

// --- Hotend V6, pendurado sob o carrinho X (fixado por uma peça impressa, não modelada aqui) ---
var alturaHotend = hotendPeca.AlturaBico + hotendPeca.AlturaBloco + hotendPeca.AlturaPescoco + hotendPeca.AlturaHeatsink;
var hotend = Add(hotendPeca, at: Placement.At(0, -25, alturaTopoCarrinhosY - alturaHotend + 10), name: "hotend_v6");
