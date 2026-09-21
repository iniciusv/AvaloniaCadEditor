# Histórico e Decisões de Arquitetura: Kernel Geométrico

Este documento registra a evolução e as decisões de design arquitetural referentes à escolha da biblioteca de encapsulamento (wrapper) do **OpenCASCADE Technology (OCCT)** para o AvaloniaCadEditor. Tivemos vários contratempos com bibliotecas de terceiros até chegarmos à solução ideal.

## 🎯 O Objetivo
Precisávamos de uma biblioteca em C# que fizesse a ponte com o motor C++ do OpenCASCADE para:
1. Criar modelos matemáticos exatos (B-Rep - Boundary Representation) como prismas e cilindros.
2. Realizar operações booleanas complexas (União, Subtração/Furo, Intersecção).
3. **Extrair a malha de triângulos (Tessellation/Triangulation)** gerada pelo kernel matemático para que pudéssemos desenhá-la manualmente em nossa interface 3D customizada do Avalonia.

---

## ✅ A Solução Atual (Aprovada)

### `OcctNet.Wrapper`
**Status:** Em uso no projeto (`AvaloniaCadEditor.Infrastructure`).
- **Por que escolhemos:** É uma biblioteca 100% de código aberto, mantida pela comunidade e gratuita. Ela utiliza P/Invoke direto para se comunicar com as DLLs nativas do OpenCASCADE.
- **Vantagens:** 
  - **Sem popups:** Por ser open-source e livre, não exibe caixas de diálogo de "Avaliação" (Evaluation).
  - **API Direta e Limpa:** Expõe classes de alto nível que precisamos (`OcctShape`, `OcctFace`, `OcctWire`) e já embute métodos facilitadores como `.Extrude()`, `.Cut()` e, crucialmente, `.Triangulate()`.
  - **Independência:** Traz as DLLs nativas compiladas prontas para uso, sem exigir que o usuário instale ferramentas pesadas de C++ ou o redistributable no sistema.

---

## 🚫 Bibliotecas Rejeitadas (A Evitar)

Durante o desenvolvimento, testamos as seguintes opções que devem ser evitadas em refatorações futuras:

### 1. `Macad.Occt` (do projeto Macad3D)
- **Por que evitamos:** Apesar de ser uma das wrappers C++/CLI mais famosas e ser de código aberto, os desenvolvedores do Macad3D **removeram propositalmente** as classes de geração de malha (como `BRepMesh_IncrementalMesh` e `Poly_Triangulation`) da API exposta. 
- **O Problema:** Nós conseguíamos realizar a matemática de furos e booleanos, mas o sistema ficava "cego". Era impossível extrair os vértices e triângulos (mesh) de volta para o C# para renderizar no Avalonia sem acoplar nossa aplicação inteira à engine de interface WPF privada e não documentada deles (`Macad.Core`/`Macad.Exchange`).

### 2. `Occt.NET` e `AnyCAD.Foundation.NET`
- **Por que evitamos:** **Licença Comercial e Popups invasivos.**
- **O Problema:** AnyCAD fornece excelentes wrappers. O `Occt.NET` chegou a funcionar perfeitamente e possuía os algoritmos de malha que o Macad deletou. No entanto, são produtos comerciais disfarçados de pacotes NuGet gratuitos. Ao instanciar as operações geométricas, a biblioteca injeta forçadamente uma janela de popup ("Evaluation Version") bloqueando a UI da aplicação. Isso quebra completamente o requisito de termos um software livre/gratuito independente.

### 3. `Silk.NET` (Bindings diretos / OpenCASCADE)
- **Por que evitamos:** Muito baixo nível e escopo diferente.
- **O Problema:** O Silk.NET é espetacular para invocar APIs de renderização nativas (OpenGL, Vulkan, DirectX). Contudo, usar bindings gerados automaticamente (como ClangSharp) em cima do gigantesco e complexo código em C++ do OpenCASCADE resulta em uma API em C# repleta de ponteiros soltos, necessidade de blocos `unsafe` e gerenciamento de memória manual. O risco de vazamento de memória (memory leaks) e crashes na aplicação seria altíssimo. O custo-benefício de manutenção não valeria a pena.

---

## 🏗️ Conclusão da Arquitetura
A decisão de isolar o Kernel Geométrico atrás da interface `IGeometryKernel` na camada de `Infrastructure` provou ser essencial. Ela nos permitiu trocar de bibliotecas 3 vezes (de Macad para AnyCAD, e de AnyCAD para OcctNet.Wrapper) sem precisar alterar sequer uma linha de código das nossas regras de negócio (`Domain`) ou da nossa interface visual (`UI`).
