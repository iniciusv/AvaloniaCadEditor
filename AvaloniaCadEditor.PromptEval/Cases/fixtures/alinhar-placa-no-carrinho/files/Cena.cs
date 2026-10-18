var carrinhoPeca = new CarrinhoMgn15H();
var placaPeca = new PlacaSuporteCarrinhoY();

// Carrinho centrado na origem em planta, base em z = 0.
var carrinho_y = Add(carrinhoPeca, at: Placement.At(-carrinhoPeca.Largura / 2, -carrinhoPeca.Comprimento / 2, 0), name: "carrinho_y");
var placa_suporte = Add(placaPeca, at: Placement.At(80, 40, 30), name: "placa_suporte", color: "#00CC6A");
