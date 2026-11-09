using Ant.GrasshopperPlugIn.Attributes;
using Ant.GrasshopperPlugIn.DataTypes;
using Ant.GrasshopperPlugIn.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using GPC.Utilities.Extensions;
using GH_IO.Serialization;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;

namespace Ant.GrasshopperPlugIn.Components
{
	public class MeshComponent : GH_TaskCapableComponent<MeshComponent.SolveResults>
	{
		public class SolveResults
		{
			public List<GH_Mesh> Value { get; set; } = new List<GH_Mesh>();
			public List<string> Errors { get; set; }
			public List<string> Warnings { get; set; }
			public List<Exception> Exceptions { get; set; }
			public double TotalTime { get; set; }
			public double PreparationTime { get; set; }
			public double GenerationTime { get; set; }
			public double ConversionTime { get; set; }
		}

		protected bool _run;
		protected Rhino.RhinoDoc _doc;

		public MeshComponent()
			: base("Planar Mesh", "PM", "Create mesh from planar surfaces", "Ant", "Mesh")
		{
			// GMesh serializes native generation globally; task mode remains opt-in in GH.
			UseTasks = false;
		}

		public override bool Write(GH_IWriter writer)
		{
			writer.SetBoolean("Ant.UseTasks", UseTasks);
			return base.Write(writer);
		}

		public override bool Read(GH_IReader reader)
		{
			bool read = base.Read(reader);
			if (reader.ItemExists("Ant.UseTasks")) UseTasks = reader.GetBoolean("Ant.UseTasks");
			return read;
		}

		public override void CreateAttributes()
		{
			ComponentOneButtonAttributes attr = new ComponentOneButtonAttributes(this, "Run");
			attr.ButtonPressed += () =>
			{
				_run = true;
				ExpireSolution(true);
			};
			m_attributes = attr;
		}

		protected override void RegisterInputParams(GH_InputParamManager pManager)
		{
			pManager.AddBrepParameter("Surfaces", "S", "List of surfaces to mesh. Surfaces must be planar with a tolerance of 1E-4", GH_ParamAccess.list);

			pManager.AddGeometryParameter("Embedded", "E", "List of embedded geometries", GH_ParamAccess.list);
			pManager[1].Optional = true;

			pManager.AddGenericParameter("Refinements", "R", "List of mesh refinements", GH_ParamAccess.list);
			pManager[2].Optional = true;

			pManager.AddIntegerParameter("Mesh Algorithm", "MA", "The algorithm used for create the mesh", GH_ParamAccess.item,
				(int)GPC.Geometry.Meshes.GMesh.GMesh.GMeshGenerateOptions.MeshAlgorithm.FrontalDelaunayForQuads);
			Param_Integer algorithmParam = (Param_Integer)pManager[3];
			Array values = Enum.GetValues(typeof(GPC.Geometry.Meshes.GMesh.GMesh.GMeshGenerateOptions.MeshAlgorithm));
			foreach (GPC.Geometry.Meshes.GMesh.GMesh.GMeshGenerateOptions.MeshAlgorithm val in values)
				algorithmParam.AddNamedValue(val.GetDescription(), (int)val);

			pManager.AddBooleanParameter("Recombine", "R", "Recombine the mesh faces to get as much quad as possibile?", GH_ParamAccess.item, true);

			pManager.AddIntegerParameter("Recombine Algorithm", "RA", "The algorithm used to recombine the faces", GH_ParamAccess.item,
				(int)GPC.Geometry.Meshes.GMesh.GMesh.GMeshGenerateOptions.RecombinationMeshAlgorithm.SimpleFullQuad);
			Param_Integer recombineParam = (Param_Integer)pManager[5];
			Array recombineValues = Enum.GetValues(typeof(GPC.Geometry.Meshes.GMesh.GMesh.GMeshGenerateOptions.RecombinationMeshAlgorithm));
			foreach (GPC.Geometry.Meshes.GMesh.GMesh.GMeshGenerateOptions.RecombinationMeshAlgorithm val in recombineValues)
				recombineParam.AddNamedValue(val.GetDescription(), (int)val);

			pManager.AddBooleanParameter("Transfinite", "T", "If true, Transfinite meshing constraints explicitly specifies the location of the nodes on the bondary", GH_ParamAccess.item, true);

			pManager.AddBooleanParameter("Refine", "R", "If true, Refine the mesh", GH_ParamAccess.item, false);

			pManager.AddNumberParameter("Size", "S", "The size of the mesh. " +
				"It is recommended to reduce the size mesh in case of constrain", GH_ParamAccess.item);


			pManager.AddNumberParameter("Tolerance", "T", "The geometry tolerance. It don't affect flatness of the surfaces but only the edges and vertices matching. " +
				"Maximum admitted value: 100. " +
				"Is recommended to use values ​​less or equals than 1 in case of embedded geometries", GH_ParamAccess.item, 0.1);

			pManager.AddBooleanParameter("Keep Running", "KR", "Run", GH_ParamAccess.item, false);
		}

		protected override void RegisterOutputParams(GH_OutputParamManager pManager)
		{
			pManager.AddMeshParameter("Mesh", "M", "The resulting mesh", GH_ParamAccess.list);
			pManager.AddTextParameter("Report", "I", "Preparation, Gmsh generation (including queue wait), conversion and total timings in seconds.", GH_ParamAccess.item);
		}

		protected override void AfterSolveInstance()
		{
			// Keep the request through every branch and through the task collection pass.
			bool completed = !InPreSolve;
			base.AfterSolveInstance();
			if (completed)
				_run = false;
		}

		protected (List<GPC.Geometry.Shape> shapes, Dictionary<GPC.Geometry.Shape, GPC.Geometry.GeometryBase[]> embeddedGeometries,
		   Dictionary<GPC.Geometry.GeometryBase, double> embeddedGeometriesMeshSize)
			PreProcessMesh(List<Brep> surfaces, List<IGH_GeometricGoo> constraints, List<IGH_GeometricGoo> refinements, double rhinoEmbedTolerance, double calcTolerance,
			double rhinoMatchTolerance, double size, ref List<string> _errors, ref List<string> _warnings, CancellationToken cancellationToken = default)
		{
			List<GPC.Geometry.Shape> shapes = new List<GPC.Geometry.Shape>();
			Dictionary<GPC.Geometry.Shape, GPC.Geometry.GeometryBase[]> embeddedGeometries = new Dictionary<GPC.Geometry.Shape, GPC.Geometry.GeometryBase[]>();
			Dictionary<GPC.Geometry.GeometryBase, double> embeddedGeometriesMeshSize = new Dictionary<GPC.Geometry.GeometryBase, double>();

			var faceBreps = new List<Brep>();
			constraints ??= new List<IGH_GeometricGoo>();
			refinements ??= new List<IGH_GeometricGoo>();
			using var constraintIndex = new BoundsIndex();
			using var refinementIndex = new BoundsIndex();
			for (int i = 0; i < constraints.Count; i++) if (constraints[i] != null) constraintIndex.Add(constraints[i].Boundingbox, i);
			for (int i = 0; i < refinements.Count; i++) if (refinements[i] != null) refinementIndex.Add(refinements[i].Boundingbox, i);
			try
			{
				cancellationToken.ThrowIfCancellationRequested();
                MeshInputGeometry.SplitFaces(surfaces, faceBreps);
				surfaces = faceBreps;
				HashSet<IGH_GeometricGoo> hashSet = new HashSet<IGH_GeometricGoo>();


				for (int b = 0; b < surfaces.Count; b++)
				{
					cancellationToken.ThrowIfCancellationRequested();
                    if (surfaces[b] == null)
						continue;

					GPC.Geometry.Polygon3d fill = null;
					List<GPC.Geometry.Polygon3d> holes = new List<GPC.Geometry.Polygon3d>();

					for (int loop = 0; loop < surfaces[b].Loops.Count; loop++)
					{
						#region Creazione della Shape

						GPC.Geometry.Polygon3d polygon = new GPC.Geometry.Polygon3d();

						using Curve curve = surfaces[b].Loops[loop].To3dCurve();
						if (curve == null)
							throw new InvalidOperationException("A surface loop could not be read.");

						if (curve.Degree > 1)
						{
							PolylineCurve polylineCurve = null;
							try
							{
								polylineCurve = ToPolylineCurve(curve, calcTolerance, size) ?? throw new InvalidOperationException("Curve conversion failed.");
							}
							catch (Exception)
							{
								_errors.Add("Invalid loop: fail to convert curve to polyline");
								continue;
							}
							if (polylineCurve != null)
							{
								for (int j = 0; j < polylineCurve.PointCount; j++)
								{
									try
									{
										if (!polygon.PointExists(new GPC.Geometry.Point3d(polylineCurve.Point(j).X, polylineCurve.Point(j).Y, polylineCurve.Point(j).Z), rhinoMatchTolerance))
											polygon.Add(new GPC.Geometry.Point3d(polylineCurve.Point(j).X, polylineCurve.Point(j).Y, polylineCurve.Point(j).Z));
									}
									catch (ArgumentException)
									{
										_errors.Add($"Polygon with additional point {polylineCurve.Point(j)} is not planar");
										continue;
									}
								}
							}
						}
						else
						{
							if (curve == null || !curve.TryGetPolyline(out Polyline pline, out double[] parameters))
							{
								_errors.Add("Invalid loop: check if there are surfaces with curved edges");
								continue;
							}

							for (int p = 0; p < pline.Count; p++)
							{
								try
								{
									if (!polygon.PointExists(pline[p].X, pline[p].Y, pline[p].Z, rhinoMatchTolerance))
										polygon.Add(pline[p].X, pline[p].Y, pline[p].Z);
								}
								catch (ArgumentException)
								{
									_errors.Add($"Polygon with additional point {pline[p]} is not planar");
									continue;
								}
							}
						}

						if (polygon.Count() < 3)
							throw new ArgumentException("A surface loop has fewer than three distinct vertices after matching.");

						if (surfaces[b].Loops[loop].LoopType == BrepLoopType.Outer)
						{
							fill = polygon;
						}
						else
						{
							holes.Add(polygon);
						}

						#endregion
					}

					if (fill != null)
					{
						GPC.Geometry.Shape shape = new GPC.Geometry.Shape(fill, holes.Count > 0 ? holes.ToArray() : null);

						shapes.Add(shape);
						List<GPC.Geometry.GeometryBase> geoms = new List<GPC.Geometry.GeometryBase>();

						#region Embedded

						if (constraints != null)
						{
							foreach (int ct in constraintIndex.Search(surfaces[b].GetBoundingBox(true), rhinoEmbedTolerance))
							{
                            cancellationToken.ThrowIfCancellationRequested();
								if (constraints[ct] == null)
									continue;

								bool embed = false;

								if (!hashSet.Contains(constraints[ct]))
								{
									if (constraints[ct] is GH_Point point && embed == false)
									{
										if (surfaces[b].ClosestPoint(point.Value, out Point3d closest, out _, out _, out _, rhinoEmbedTolerance, out _) && closest.DistanceTo(point.Value) <= rhinoEmbedTolerance)
										{
											GPC.Geometry.Point3d p = new GPC.Geometry.Point3d(closest.X, closest.Y, closest.Z);
											geoms.Add(p);
											hashSet.Add(constraints[ct]);
											embed = true;
										}
									}
									else if (constraints[ct] is GH_Line line)
                                    {
                                        if (MeshInputGeometry.TryProjectLine(surfaces[b], line.Value, rhinoEmbedTolerance, out var projected))
                                        {
                                            geoms.Add(new GPC.Geometry.Line3d(
                                                new GPC.Geometry.Point3d(projected.FromX, projected.FromY, projected.FromZ),
                                                new GPC.Geometry.Point3d(projected.ToX, projected.ToY, projected.ToZ)));
                                            hashSet.Add(constraints[ct]);
                                        }
                                    }
                                    else if (constraints[ct] is GH_Curve curve)
									{
										if (MeshInputGeometry.TryProjectCurve(surfaces[b], curve.Value,
											rhinoEmbedTolerance, calcTolerance, size, out var segments))
										{
											geoms.AddRange(segments.Select(l => (GPC.Geometry.GeometryBase)new GPC.Geometry.Line3d(
												new GPC.Geometry.Point3d(l.FromX, l.FromY, l.FromZ),
												new GPC.Geometry.Point3d(l.ToX, l.ToY, l.ToZ))));
											hashSet.Add(constraints[ct]);
										}
									}
									else if (constraints[ct] is GH_Surface || constraints[ct] is GH_Brep)
                                    {
                                        var region = constraints[ct] is GH_Surface s ? s.Value : ((GH_Brep)constraints[ct]).Value;
                                        if (MeshInputGeometry.TryProjectRegion(surfaces[b], region, rhinoEmbedTolerance, calcTolerance, out var projected))
                                        {
                                            geoms.AddRange(projected);
                                            hashSet.Add(constraints[ct]);
                                        }
                                    }
                                }
							}
						}

						#endregion

						#region Refinement

                        foreach (int refinementId in refinementIndex.Search(surfaces[b].GetBoundingBox(true), rhinoEmbedTolerance))
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var refinement = refinements[refinementId];
                            if (refinement == null || hashSet.Contains(refinement)) continue;
                            if (!MeshInputGeometry.TryProjectRefinement(surfaces[b], refinement, rhinoEmbedTolerance,
                                calcTolerance, size, out var projected, out double localSize)) continue;
                            foreach (var candidate in projected)
                            {
                                var existing = geoms.FirstOrDefault(g => g.Equals(candidate));
                                var geometry = existing ?? candidate;
                                if (existing == null) geoms.Add(geometry);
                                embeddedGeometriesMeshSize[geometry] = Math.Min(localSize,
                                    embeddedGeometriesMeshSize.TryGetValue(geometry, out double previous) ? previous : double.PositiveInfinity);
                            }
                            hashSet.Add(refinement);
                        }

                        #endregion

						if (geoms.Count > 0)
						{
							embeddedGeometries.Add(shape, geoms.ToArray());
						}

						geoms.Clear();
					}
					else
						_errors.Add($"Face {b} has no valid outer loop.");
				}

				int unmatchedConstraints = constraints.Count(g => g != null && !hashSet.Contains(g));
				int unmatchedRefinements = refinements.Count(g => g != null && !hashSet.Contains(g));
				if (unmatchedConstraints > 0 || unmatchedRefinements > 0)
					_warnings.Add($"Unmatched embedded geometries: {unmatchedConstraints}; refinements: {unmatchedRefinements}. " +
						"Each geometry must fit entirely on one face without crossing holes. Check its type, or use Clean Geometry to project and split it.");

				if (shapes.Count() == 0)
				{
					_errors.Add("There is no valid surface to mesh");
				}

			}
			catch (OperationCanceledException) { throw; }
            catch (Exception e)
			{
				_errors.Add($"Mesh input preparation failed: {e.Message}");
			}
			finally
			{
				foreach (var face in faceBreps)
					face.Dispose();
			}

			return (shapes, embeddedGeometries, embeddedGeometriesMeshSize);
		}

		protected List<GH_Mesh> PostProcessMesh(List<GPC.Geometry.Meshes.Mesh> meshes, ref List<string> _errors, CancellationToken cancellationToken = default)
		{
			var converted = new List<GH_Mesh>(meshes.Count);
			try
			{
				// Avoid a second layer of blocking tasks for a linear vertex/face conversion.
				foreach (var mesh in meshes)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    converted.Add(new GH_Mesh(mesh.ConvertToRhinoMesh()));
                }
                cancellationToken.ThrowIfCancellationRequested();
				return converted;
			}
			catch (OperationCanceledException) { foreach (var mesh in converted) mesh.Value?.Dispose(); throw; }
            catch (Exception e)
			{
				_errors.Add(e.Message);

				foreach (var mesh in converted) mesh.Value?.Dispose();
				return new List<GH_Mesh>();
			}
		}

		protected SolveResults ComputeMesh(List<Brep> surfaces, List<IGH_GeometricGoo> constraints, List<IGH_GeometricGoo> refinements,
			GPC.Geometry.Meshes.GMesh.GMesh.GMeshGenerateOptions generateOptions, double rhinoEmbedTolerance, double calcTolerance, double rhinoMatchTolerance, double size, CancellationToken cancellationToken = default)
		{
			try
			{
				cancellationToken.ThrowIfCancellationRequested();
                var clock = Stopwatch.StartNew();

				SolveResults results = new SolveResults();

				List<string> errors = new List<string>();
				List<string> warnings = new List<string>();

				(List<GPC.Geometry.Shape> shapes, Dictionary<GPC.Geometry.Shape, GPC.Geometry.GeometryBase[]> embeddedGeometries,
					Dictionary<GPC.Geometry.GeometryBase, double> embeddedGeometriesMeshSize) =
					PreProcessMesh(surfaces, constraints, refinements, rhinoEmbedTolerance, calcTolerance, rhinoMatchTolerance, size, ref errors, ref warnings, cancellationToken);
				results.PreparationTime = clock.Elapsed.TotalSeconds;

				if (errors.Count > 0)
				{
					results.Errors = errors;
					results.Warnings = warnings;
					results.TotalTime = clock.Elapsed.TotalSeconds;
					return results;
				}

				var generated = NativeMeshGate.Execute(cancellationToken, () =>
                {
                    bool success = GPC.Geometry.Meshes.GMesh.GMesh.Generate(shapes, embeddedGeometries, embeddedGeometriesMeshSize, generateOptions,
                        out List<GPC.Geometry.Meshes.Mesh> nativeMeshes, out GPC.Geometry.Meshes.GMesh.GMesh.GMeshGenerateMeshStatus nativeStatus);
                    return (success, nativeMeshes, nativeStatus);
                });
                bool status = generated.success;
                var meshes = generated.nativeMeshes;
                var meshStatus = generated.nativeStatus;
                results.GenerationTime = clock.Elapsed.TotalSeconds - results.PreparationTime;

				if (status)
				{
					List<GH_Mesh> GHMeshes = PostProcessMesh(meshes, ref errors, cancellationToken);
					results.Value = GHMeshes;
				}

				warnings.AddRange(meshStatus.Warnings);

				errors.AddRange(meshStatus.CustomErrorMessages.Select(i => i));

				results.Errors = errors;
				results.Warnings = warnings;
				results.Exceptions = meshStatus.Exceptions;
				results.TotalTime = clock.Elapsed.TotalSeconds;
				results.ConversionTime = results.TotalTime - results.PreparationTime - results.GenerationTime;

				return results;
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new SolveResults
                {
                    Errors = new List<string>(),
                    Warnings = new List<string> { "Meshing cancelled; no result was published." },
                    Exceptions = new List<Exception>(),
                    Value = new List<GH_Mesh>()
                };
            }
            catch (Exception e)
			{
				SolveResults results = new SolveResults
				{
					Exceptions = new List<Exception>() { e },
					Errors = new List<string>() { e.Message },
					Value = new List<GH_Mesh>()
				};

				return results;
			}
		}

		protected override void SolveInstance(IGH_DataAccess data)
		{
			#region Input

			double tolerance = 0.1;
			List<Brep> surfaces = new List<Brep>();
			if (!data.GetDataList(0, surfaces))
				return;

			List<IGH_GeometricGoo> constraints = new List<IGH_GeometricGoo>();
			data.GetDataList(1, constraints);

			List<IGH_GeometricGoo> refinements = new List<IGH_GeometricGoo>();
			data.GetDataList(2, refinements);

			int meshAlgorithm = 0;
			if (!data.GetData(3, ref meshAlgorithm))
				return;

			bool recombine = false;
			if (!data.GetData(4, ref recombine))
				return;

			int recombineAlgorithm = 0;
			if (!data.GetData(5, ref recombineAlgorithm))
				return;

			bool transfinite = false;
			if (!data.GetData(6, ref transfinite))
				return;

			bool refine = false;
			if (!data.GetData(7, ref refine))
				return;

			double size = 0;
			if (!data.GetData(8, ref size))
				return;

			if (!data.GetData(9, ref tolerance))
				return;

			bool keepRunning = false;
			if (!data.GetData(10, ref keepRunning))
				return;


			#endregion

			if (_run || keepRunning)
			{
				if (!double.IsFinite(size) || size <= 0 || !double.IsFinite(tolerance) || tolerance <= 0 || tolerance > 100)
				{
					AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Size must be positive and finite; tolerance must be between 0 (exclusive) and 100.");
					return;
				}

				#region Gestione fattore di scala & Opzioni mesh

                _doc = Rhino.RhinoDoc.ActiveDoc;
                var tolerances = new MeshToleranceSettings(tolerance, size, _doc?.ModelUnitSystem ?? Rhino.UnitSystem.None);
                double scaleGeometryFactor = tolerances.GeometryScale;
                double calcTolerance = tolerances.Calculation;
                double scaleMeshFactor = tolerances.MeshScale;
                double sizeScale = tolerances.MeshSize;
                double matchMeshTolerance = tolerances.MeshMatch;
                double matchGeomAndMesh = tolerances.GeometryAndMeshMatch;
                double rhinoEmbedTolerance = tolerances.Projection;
                double rhinoMatchTolerance = tolerances.RhinoMatch;
				// tolleranza di OpenCascade: 10E-7,
				// tolleranza di Gmsh: 10E-8,
				// tolleranza di GPCGeometry 10E-4
				// la tolleranza di gmsh possiamo cambiarla attraverso la funzione Geometry.Tolerance presente nel meshatore tra le opzioni del GenerateMeshOptions
				// usiamo questo metodo dello scalare < 1 la geometria per poter gestire la tolleranza di OCC. riducendo il modello, "aumentiamo" la tolleranza di OCC. 
				// questo ci permette di uniformare il comportamento di tutto il codice e non avere problemi di match di punti.
				// la tolleranza base è quella di OCC. l'utente, inserendo una tolleranza, scala il modello di una potenza tale da matchare la tolleranza di input e quella di OCC.
				// La tolleranza di gmsh si comporta di conseguenza.
				// si consiglia una tolleranza di 10-3/10-4 (o inferiori) perchè le funzioni di matching lavorano con 100 volte questo valore, quindi non è consigliato arrivare a valori di 0.1/1.

				GPC.Geometry.Meshes.GMesh.GMesh.GMeshGenerateOptions generateOptions = new GPC.Geometry.Meshes.GMesh.GMesh.GMeshGenerateOptions
				{
					GeometryBaseScaleFactor = scaleGeometryFactor,
					Algorithm = (GPC.Geometry.Meshes.GMesh.GMesh.GMeshGenerateOptions.MeshAlgorithm)meshAlgorithm,
					MeshSize = sizeScale,
					Recombine = recombine,
					RecombinationAlgorithm = (GPC.Geometry.Meshes.GMesh.GMesh.GMeshGenerateOptions.RecombinationMeshAlgorithm)recombineAlgorithm,
					Refine = refine,
					Tolerance = calcTolerance,
					MatchMeshTolerance = matchMeshTolerance,
					ToleranceInitialDelaunay = matchGeomAndMesh,
					MatchGeomAndMesh = matchGeomAndMesh,
					Transfinite = transfinite,
					HealShapes = true,
					OptimizeAlgorithm = GPC.Geometry.Meshes.GMesh.GMesh.GMeshGenerateOptions.MeshOptimize.Netgen,
					OptimizeIteration = 1,
					MeshScalingFactor = scaleMeshFactor
				};
				generateOptions.RandomFactor *= scaleGeometryFactor;

				#endregion

				SolveResults result = new SolveResults();

				// Componente prova ad usare i task, viene lanciato il thread e poi ritorna.
				// Poi il componente farà un altro passaggio per raccogliere i risultati
				var solveToken = CancelToken;
                if (InPreSolve)
				{
					Task<SolveResults> task = Task.Run(() => ComputeMesh(surfaces, constraints, refinements, generateOptions, rhinoEmbedTolerance,
						calcTolerance, rhinoMatchTolerance, size, solveToken));
					TaskList.Add(task);
					return;
				}

				// risultati non disponibili, vengono calcolati direttamente senza task
				if (!GetSolveResults(data, out result))
				{
					result = ComputeMesh(surfaces, constraints, refinements, generateOptions, rhinoEmbedTolerance, calcTolerance, rhinoMatchTolerance, size, solveToken);
				}


				// Cancellation may arrive after a task completes but before GH collects its output.
                if (solveToken.IsCancellationRequested && result != null)
                {
                    foreach (var mesh in result.Value) mesh.Value?.Dispose();
                    result.Value.Clear();
                    result.Errors = new List<string>();
                    result.Warnings = new List<string> { "Meshing cancelled; no result was published." };
                    result.Exceptions = new List<Exception>();
                }
                // Output risultati
				if (result != null)
				{
					if (result.Errors != null)
					{
						foreach (string error in result.Errors)
						{
							AddRuntimeMessage(GH_RuntimeMessageLevel.Error, error);
						}
					}

					if (result.Warnings != null)
					{
						foreach (string warning in result.Warnings)
						{
							AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);
						}
					}

					data.SetDataList(0, result.Value ?? new List<GH_Mesh>());
					data.SetData(1, FormattableString.Invariant($"Preparation: {result.PreparationTime:F4} s; Gmsh including wait: {result.GenerationTime:F4} s; conversion: {result.ConversionTime:F4} s; total: {result.TotalTime:F4} s. GH tasks: {UseTasks}. Native generation is serialized."));
				}

			}
		}

		public static PolylineCurve ToPolylineCurve(Curve curve, double tolerance, double size)
		{
			return curve.ToPolyline(0, 0, Math.PI / 8.0, 1.414, 0, 0, 100 * tolerance, 1.0 * size, false);
		}

		protected override System.Drawing.Bitmap Icon => Properties.Resources.MeshIcon;

		public override Guid ComponentGuid => new Guid("c7a02ae3-7be3-41fb-83c2-41d1d455b433");

		public override GH_Exposure Exposure => GH_Exposure.primary;
	}
}
