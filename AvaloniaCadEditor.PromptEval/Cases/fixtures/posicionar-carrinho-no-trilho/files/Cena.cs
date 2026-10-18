var trilhoXPeca = new TrilhoMgn15(Comprimento: 200);
var carrinhoPeca = new CarrinhoMgn15H();
var alturaApoio = 20.0;

// Trilho do eixo X, girado para correr no sentido X, apoiado a 20 mm de altura.
var trilho_x = Add(trilhoXPeca, at: Placement.At(-100, 60, alturaApoio, rotationZDegrees: -90), name: "trilho_x");
var carrinho_x = Add(carrinhoPeca, name: "carrinho_x");
