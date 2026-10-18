var motorPeca = new MotorNema17();

// Motor de pé, eixo para cima (+Z): face da frente em z = 40.
var motor_x = Add(motorPeca, at: Placement.At(50, -30, 0), name: "motor_x");
