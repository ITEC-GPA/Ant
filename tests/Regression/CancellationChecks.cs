using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ant.GrasshopperPlugIn.Components;
using Ant.GrasshopperPlugIn.Helpers;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

internal static class CancellationChecks
{
    static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Cancelled(Action action)
    {
        try { action(); } catch (OperationCanceledException) { return; }
        throw new Exception("Cancellation was not observed");
    }
    sealed class Probe : MeshComponent
    {
        internal SolveResults Compute(CancellationToken token) => ComputeMesh(null,null,null,null,1e-4,1e-7,1e-3,1,token);
        internal void Prepare(CancellationToken token)
        {
            var errors = new List<string>(); var warnings = new List<string>();
            PreProcessMesh(new List<Brep>(),null,null,1e-4,1e-7,1e-3,1,ref errors,ref warnings,token);
        }
        internal void Convert(CancellationToken token)
        {
            var errors = new List<string>();
            PostProcessMesh(new List<GPC.Geometry.Meshes.Mesh>(),ref errors,token);
        }
    }
    internal static void Run(Action<string, Action> check)
    {
        check("cancellation: compute rejects cancelled request before touching inputs", () => {
            using var source = new CancellationTokenSource(); source.Cancel();
            var result = new Probe().Compute(source.Token);
            Require(result.Value.Count == 0 && result.Errors.Count == 0 && result.Exceptions.Count == 0 && result.Warnings.Single().Contains("cancelled"), "Cancelled request was reported as a failure");
        });
        check("cancellation: preparation propagates cancellation", () => {
            using var source = new CancellationTokenSource(); source.Cancel();
            Cancelled(() => new Probe().Prepare(source.Token));
        });
        check("cancellation: conversion propagates cancellation", () => {
            using var source = new CancellationTokenSource(); source.Cancel();
            Cancelled(() => new Probe().Convert(source.Token));
        });
        check("cancellation: pre-cancelled native request never starts", () => {
            using var source = new CancellationTokenSource(); source.Cancel(); bool called = false;
            Cancelled(() => NativeMeshGate.Execute(source.Token, () => { called=true; return 1; }));
            Require(!called, "Native generation started");
        });
        check("cancellation: running native work finishes but its result is discarded", () => {
            using var source = new CancellationTokenSource(); bool finished = false;
            Cancelled(() => NativeMeshGate.Execute(source.Token, () => { source.Cancel(); finished=true; return 42; }));
            Require(finished && NativeMeshGate.Execute(CancellationToken.None, () => 7) == 7, "Native completion/release failed");
        });
        check("cancellation: waiting request can cancel without entering native code", () => {
            using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
            using var source = new CancellationTokenSource();
            var first = Task.Run(() => NativeMeshGate.Execute(CancellationToken.None, () => { entered.Set(); release.Wait(); return 1; }));
            try {
                Require(entered.Wait(TimeSpan.FromSeconds(5)), "First call did not enter");
                bool called = false;
                using var waiting = new ManualResetEventSlim();
                var second = Task.Run(() => { waiting.Set(); Cancelled(() => NativeMeshGate.Execute(source.Token, () => { called=true; return 2; })); });
                Require(waiting.Wait(TimeSpan.FromSeconds(5)) && !second.Wait(50), "Second request did not wait");
                source.Cancel();
                Require(second.Wait(TimeSpan.FromSeconds(5)) && !called, "Queued call did not cancel");
            }
            finally { release.Set(); Require(first.Wait(TimeSpan.FromSeconds(5)), "First call did not finish"); }
        });
        check("cancellation: native exception releases gate", () => {
            try { NativeMeshGate.Execute<int>(CancellationToken.None, () => throw new InvalidOperationException("test")); }
            catch (InvalidOperationException) { }
            Require(NativeMeshGate.Execute(CancellationToken.None, () => 9) == 9, "Gate was not released");
        });
    }
}
