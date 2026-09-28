using System;
using System.Collections.Generic;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using AvaloniaCadEditor.Tests.Extensions;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

public class OpenCascadeKernelTests
{
    [Fact]
    public void ExtrusaoRetangulo_DeveGerarMalha3DValidaComTriangulos()
    {
        // Arrange
        var rect = new RectangleProfile(new Point3D(0, 0, 0), width: 10, height: 20);
        var extrude = new ExtrudeFeature(rect, height: 30.0);
        var kernel = new OpenCascadeGeometryKernel();

        // Act
        var mesh = kernel.BuildFeatureMesh(extrude);

        // Assert: A box has 6 faces * 2 triangles = 12 triangles minimum
        mesh.Should().NotBeNull();
        mesh.VertexCount.Should().BeGreaterThan(0);
        mesh.TriangleCount.Should().BeGreaterThanOrEqualTo(12);
        mesh.Positions.Length.Should().Be(mesh.VertexCount * 3);
        mesh.Indices.Length.Should().Be(mesh.TriangleCount * 3);
    }

    [Fact]
    public void ExtrusaoCirculo_DeveGerarCilindro3DComMalha()
    {
        // Arrange
        var circle = new CircleProfile(new Point3D(0, 0, 0), radius: 5.0);
        var extrude = new ExtrudeFeature(circle, height: 15.0);
        var kernel = new OpenCascadeGeometryKernel();

        // Act
        var mesh = kernel.BuildFeatureMesh(extrude);

        // Assert
        mesh.Should().NotBeNull();
        mesh.VertexCount.Should().BeGreaterThan(0);
        mesh.TriangleCount.Should().BeGreaterThan(12); // Cylinder tessellates into many triangles
    }

    // --- Regressão da orientação de tesselação (ver TessellateShape/FixTriangleWinding) ---
    // A tesselação do OCCT precisa de correção de sentido de giro por componente conexo (não a
    // heurística antiga "normal aponta para longe do centróide de todos os vértices", que falha
    // para peças côncavas, com furo, ou boolean com vários sólidos). Sem isso, o volume calculado
    // por integração (teorema da divergência) sai errado silenciosamente, mesmo a malha "parecendo"
    // certa visualmente e a IoU (baseada em paridade de raycasting) não denunciando nada.

    [Fact]
    public void UniaoComDegrauEmZ_VolumeDeveSerExato()
    {
        var baseBlock = new BoxFeature(new Point3D(0, 0, 0), width: 20, length: 20, height: 5);
        var topBlock = new BoxFeature(new Point3D(5, 5, 5), width: 10, length: 10, height: 10);
        var stepped = BooleanFeature.CreateUnion("Degrau", baseBlock, topBlock);
        var kernel = new OpenCascadeGeometryKernel();

        var mesh = kernel.BuildFeatureMesh(stepped);
        double volume = MeshVolumeCalculator.ComputeVolume(mesh);

        volume.Should().BeApproximately(3000.0, 3000.0 * 0.01);
    }

    [Fact]
    public void UniaoDeSolidosDesconexos_VolumeDeveSomarOsDois()
    {
        var far1 = new BoxFeature(new Point3D(0, 0, 0), width: 5, length: 5, height: 5);
        var far2 = new BoxFeature(new Point3D(100, 100, 100), width: 5, length: 5, height: 5);
        var farUnion = BooleanFeature.CreateUnion("Far", far1, far2);
        var kernel = new OpenCascadeGeometryKernel();

        var mesh = kernel.BuildFeatureMesh(farUnion);
        double volume = MeshVolumeCalculator.ComputeVolume(mesh);

        volume.Should().BeApproximately(250.0, 250.0 * 0.01);
    }

    [Fact]
    public void CaixaMenosCilindro_VolumeDeveSerExato()
    {
        var box = new BoxFeature(new Point3D(0, 0, 0), width: 10, length: 10, height: 10);
        var hole = new CylinderFeature(new Point3D(5, 5, -1), new Vector3D(0, 0, 1), radius: 2, height: 12);
        var cut = BooleanFeature.CreateDifference("Cut", box, hole);
        var kernel = new OpenCascadeGeometryKernel();

        var mesh = kernel.BuildFeatureMesh(cut);
        double volume = MeshVolumeCalculator.ComputeVolume(mesh);
        double expected = 10 * 10 * 10 - Math.PI * 2 * 2 * 10;

        volume.Should().BeApproximately(expected, expected * 0.01);
    }

    [Fact]
    public void ExtrusaoDePoligonoConcavo_VolumeDeveSerExato()
    {
        // Perfil em "L" (côncavo) — a heurística antiga de centróide inverte parte dos
        // triângulos desse tipo de perfil, subestimando o volume pela metade ou mais.
        var perfilL = new PolygonProfile(new List<Point3D>
        {
            new Point3D(0, 0, 0), new Point3D(10, 0, 0), new Point3D(10, 0, 4),
            new Point3D(4, 0, 4), new Point3D(4, 0, 16), new Point3D(0, 0, 16)
        });
        var extrude = new ExtrudeFeature(perfilL, height: 20.0, direction: new Vector3D(0, 20, 0));
        var kernel = new OpenCascadeGeometryKernel();

        var mesh = kernel.BuildFeatureMesh(extrude);
        double volume = MeshVolumeCalculator.ComputeVolume(mesh);

        volume.Should().BeApproximately(1760.0, 1760.0 * 0.01);
    }
}
