var xTrilho = 120.0;
var alturaBase = 40.0;

var placa_a = Add(new PlacaFuros(), at: Placement.At(xTrilho - 60, 20, alturaBase), name: "placa_a");
var placa_b = Add(new PlacaFuros(Espelhada: true), at: Placement.At(xTrilho + 15, 20, alturaBase + 2.5, rotationZDegrees: 180), name: "placa_b");
