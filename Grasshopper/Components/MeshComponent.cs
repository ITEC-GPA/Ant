using Ant.GrasshopperPlugIn.Attributes;
using Ant.GrasshopperPlugIn.DataTypes;
using Ant.GrasshopperPlugIn.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using GPC.Utilities.Extensions;
using Rhino.Geometry;
//using Sentry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Ant.GrasshopperPlugIn.Components
{
	public class MeshComponent : GH_TaskCapableComponent<MeshComponent.SolveResults>
	{
		public class SolveResults
		{
			public List<GH_Mesh> Value { get; set; }
			public List<string> Errors { get; set; }
			public List<string> Warnings { get; set; }
			public List<Exception> Exceptions { get; set; }
			public double TotalTime { get; set; }
		}

		protected bool _run;
		protected Rhino.RhinoDoc _doc;

		public MeshComponent()
			: base("Planar Mesh", "PM", "Create mesh from planar surfaces", "Ant", "Mesh")
		{

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
		}

		protected override void BeforeSolveInstance()
		{
			base.BeforeSolveInstance();
		}

		protected (List<GPC.Geometry.Shape> shapes, Dictionary<GPC.Geometry.Shape, GPC.Geometry.GeometryBase[]> embeddedGeometries,
		   Dictionary<GPC.Geometry.GeometryBase, double> embeddedGeometriesMeshSize)
			PreProcessMesh(List<Brep> surfaces, List<IGH_GeometricGoo> constraints, List<IGH_GeometricGoo> refinements, double rhinoEmbedTolerance, double calcTolerance,
			double rhinoMatchTolerance, double size, ref List<string> _errors, ref List<string> _warnings)
		{
			List<GPC.Geometry.Shape> shapes = new List<GPC.Geometry.Shape>();
			Dictionary<GPC.Geometry.Shape, GPC.Geometry.GeometryBase[]> embeddedGeometries = new Dictionary<GPC.Geometry.Shape, GPC.Geometry.GeometryBase[]>();
			Dictionary<GPC.Geometry.GeometryBase, double> embeddedGeometriesMeshSize = new Dictionary<GPC.Geometry.GeometryBase, double>();

			try
			{
				HashSet<IGH_GeometricGoo> hashSet = new HashSet<IGH_GeometricGoo>();
				int embCountToRemove = 0;

				for (int b = 0; b < surfaces.Count; b++)
				{
					if (surfaces[b] == null)
						continue;

					GPC.Geometry.Polygon3d fill = null;
					List<GPC.Geometry.Polygon3d> holes = new List<GPC.Geometry.Polygon3d>();

					for (int loop = 0; loop < surfaces[b].Loops.Count; loop++)
					{
						#region Creazione della Shape

						GPC.Geometry.Polygon3d polygon = new GPC.Geometry.Polygon3d();

						Curve curve = surfaces[b].Loops[loop].To3dCurve();

						if (curve.Degree > 1)
						{
							PolylineCurve polylineCurve = null;
							try
							{
								polylineCurve = ToPolylineCurve(curve, calcTolerance, size);
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
							for (int ct = 0; ct < constraints.Count; ct++)
							{
								if (constraints[ct] == null)
									continue;

								bool embed = false;

								if (!hashSet.Contains(constraints[ct]))
								{
									if (constraints[ct] is GH_Point point && embed == false)
									{
										if (surfaces[b].ClosestPoint(point.Value, out Point3d closest, out _, out _, out _, rhinoEmbedTolerance, out _))
										{
											GPC.Geometry.Point3d p = new GPC.Geometry.Point3d(closest.X, closest.Y, closest.Z);
											geoms.Add(p);
											hashSet.Add(constraints[ct]);
											embed = true;
										}
									}
									else if (constraints[ct] is GH_Line line && embed == false)
									{
										var startPt = line.Value.From;
										var endPt = line.Value.To;

										if (surfaces[b].ClosestPoint(startPt, out Point3d cp1, out _, out _, out _, rhinoEmbedTolerance, out _) &&
											surfaces[b].ClosestPoint(endPt, out Point3d cp2, out _, out _, out _, rhinoEmbedTolerance, out _))
										{
											if (cp1 != null && cp2 != null)
											{
												GPC.Geometry.Line3d l = new GPC.Geometry.Line3d(new GPC.Geometry.Point3d(cp1.X, cp1.Y, cp1.Z), new GPC.Geometry.Point3d(cp2.X, cp2.Y, cp2.Z));
												geoms.Add(l);
												hashSet.Add(constraints[ct]);
												embed = true;
											}
											else if ((cp1 == null && cp2 != null) || (cp2 == null && cp1 != null))
											{
												_warnings.Add($"Line with start: {cp1.X}, {cp1.Y}, {cp1.Z} and end: {cp2.X}, {cp2.Y}, {cp2.Z} is not valid: it must be on only one shape");
												continue;
											}
										}
									}
									else if (constraints[ct] is GH_Curve curve && embed == false)
									{
										if (curve.Value is LineCurve lc && embed == false)
										{
											var ptStart = lc.PointAtStart;
											var ptEnd = lc.PointAtEnd;

											if (surfaces[b].ClosestPoint(ptStart, out Point3d cp1, out _, out _, out _, rhinoEmbedTolerance, out _) &&
												surfaces[b].ClosestPoint(ptEnd, out Point3d cp2, out _, out _, out _, rhinoEmbedTolerance, out _))
											{
												if (cp1 != null && cp2 != null)
												{
													GPC.Geometry.Line3d l = new GPC.Geometry.Line3d(new GPC.Geometry.Point3d(cp1.X, cp1.Y, cp1.Z), new GPC.Geometry.Point3d(cp2.X, cp2.Y, cp2.Z));
													geoms.Add(l);
													hashSet.Add(constraints[ct]);
													embed = true;
												}
												else if ((cp1 == null && cp2 != null) || (cp2 == null && cp1 != null))
												{
													_warnings.Add($"Line with start: {cp1.X}, {cp1.Y}, {cp1.Z} and end: {cp2.X}, {cp2.Y}, {cp2.Z} is not valid: it must be on only one shape");
													continue;
												}
											}
										}
										else if (curve.Value is PolylineCurve pline)
										{
											bool add = true;

											for (int i = 0; i < pline.PointCount - 1; i++)
											{
												Point3d p1 = pline.Point(i);
												Point3d p2 = pline.Point(i + 1);

												if (surfaces[b].ClosestPoint(p1, out Point3d cp1, out _, out _, out _, rhinoEmbedTolerance, out _) &&
													surfaces[b].ClosestPoint(p2, out Point3d cp2, out _, out _, out _, rhinoEmbedTolerance, out _))
												{
													if (cp1 != null && cp2 != null)
													{
														GPC.Geometry.Line3d l = new GPC.Geometry.Line3d(new GPC.Geometry.Point3d(cp1.X, cp1.Y, cp1.Z), new GPC.Geometry.Point3d(cp2.X, cp2.Y, cp2.Z));
														geoms.Add(l);
														embed = true;

														if (i != 0)
															embCountToRemove++;
													}
													else if ((cp1 == null && cp2 != null) || (cp2 == null && cp1 != null))
													{
														_warnings.Add($"Line with start: {cp1.X}, {cp1.Y}, {cp1.Z} and end: {cp2.X}, {cp2.Y}, {cp2.Z} is not valid: it must be on only one shape");
														add = false;
														continue;
													}
												}
											}

											if (add)
												hashSet.Add(constraints[ct]);
										}
										else if (curve.Value is Curve c && embed == false)
										{
											bool add = true;
											PolylineCurve polylineCurve = ToPolylineCurve(c, calcTolerance, size);

											for (int i = 0; i < polylineCurve.PointCount - 1; i++)
											{
												Point3d p1 = polylineCurve.Point(i);
												Point3d p2 = polylineCurve.Point(i + 1);

												if (surfaces[b].ClosestPoint(p1, out Point3d cp1, out _, out _, out _, rhinoEmbedTolerance, out _) &&
													surfaces[b].ClosestPoint(p2, out Point3d cp2, out _, out _, out _, rhinoEmbedTolerance, out _))
												{
													if (cp1 != null && cp2 != null)
													{
														GPC.Geometry.Line3d l = new GPC.Geometry.Line3d(new GPC.Geometry.Point3d(cp1.X, cp1.Y, cp1.Z), new GPC.Geometry.Point3d(cp2.X, cp2.Y, cp2.Z));
														geoms.Add(l);
														embed = true;

														if (i != 0)
															embCountToRemove++;
													}
													else if ((cp1 == null && cp2 != null) || (cp2 == null && cp1 != null))
													{
														_warnings.Add($"Line with start: {cp1.X}, {cp1.Y}, {cp1.Z} and end: {cp2.X}, {cp2.Y}, {cp2.Z} is not valid: it must be on only one shape");
														add = false;
														continue;
													}
												}
											}

											if (add)
												hashSet.Add(constraints[ct]);
										}
									}
									else if (constraints[ct] is GH_Surface embSurf)
									{
										GPC.Geometry.Point3d[] points = new GPC.Geometry.Point3d[embSurf.Value.Vertices.Count];
										bool added = true;

										for (int i = 0; i < embSurf.Value.Vertices.Count; i++)
										{
											var pt = embSurf.Value.Vertices[i].Location;
											if (surfaces[b].ClosestPoint(pt, out Point3d cp, out _, out _, out _, rhinoEmbedTolerance, out _))
												points[i] = new GPC.Geometry.Point3d(cp.X, cp.Y, cp.Z);

											else if (cp == null)
											{
												added = false;
												_warnings.Add($"Point: {pt.X}, {pt.Y}, {pt.Z} of surface {embSurf} is not valid: it must be on one shape");
												continue;
											}
										}
										if (added)
										{
											GPC.Geometry.Shape s = new GPC.Geometry.Shape(new GPC.Geometry.Polygon3d(points, calcTolerance));

											geoms.Add(s);
											hashSet.Add(constraints[ct]);
											embed = true;
										}
									}
									else if (constraints[ct] is GH_Brep brep)
									{
										GPC.Geometry.Point3d[] points = new GPC.Geometry.Point3d[brep.Value.Vertices.Count];
										bool added = true;

										for (int i = 0; i < brep.Value.Vertices.Count; i++)
										{
											bool addedBuffer = true;

											var pt = brep.Value.Vertices[i].Location;
											addedBuffer = surfaces[b].ClosestPoint(pt, out Point3d cp, out _, out _, out _, rhinoEmbedTolerance, out _);

											if (addedBuffer)
												points[i] = new GPC.Geometry.Point3d(cp.X, cp.Y, cp.Z);
											else
												added = false;
										}
										if (added)
										{
											GPC.Geometry.Shape s = new GPC.Geometry.Shape(new GPC.Geometry.Polygon3d(points, calcTolerance));

											geoms.Add(s);
											hashSet.Add(constraints[ct]);
											embed = true;
										}
									}
								}
							}
						}

						#endregion

						#region Refinement

						if (refinements != null)
						{
							for (int r = 0; r < refinements.Count; r++)
							{
								if (refinements[r] == null)
									continue;
								if (!hashSet.Contains(refinements[r]))
								{
									if (refinements[r] is MeshPointRefinement point)
									{
										if (surfaces[b].ClosestPoint(point.Geometry, out Point3d closest, out _, out _, out _, rhinoEmbedTolerance, out _))
										{
											GPC.Geometry.Point3d pt = new GPC.Geometry.Point3d(closest.X, closest.Y, closest.Z);
											if (!geoms.Contains(pt))
											{
												geoms.Add(pt);
												embeddedGeometriesMeshSize.Add(pt, point.Size);
												hashSet.Add(refinements[r]);
											}
										}
										else
										{
											_warnings.Add($"point {closest.X}, {closest.Y}, {closest.Z} is not on plane or internal");
										}
									}
									else if (refinements[r] is MeshLineRefinement line)
									{
										var ptStart = line.Geometry.From;
										var ptEnd = line.Geometry.To;

										if (surfaces[b].ClosestPoint(ptStart, out Point3d cp1, out _, out _, out _, rhinoEmbedTolerance, out _) &&
											surfaces[b].ClosestPoint(ptEnd, out Point3d cp2, out _, out _, out _, rhinoEmbedTolerance, out _))
										{
											GPC.Geometry.Line3d line3d = new GPC.Geometry.Line3d(new GPC.Geometry.Point3d(cp1.X, cp1.Y, cp1.Z), new GPC.Geometry.Point3d(cp2.X, cp2.Y, cp2.Z));
											if (!geoms.Contains(line3d))
											{
												if (cp1 != null && cp2 != null)
												{
													geoms.Add(line3d);
													embeddedGeometriesMeshSize.Add(line3d, line.Size);
													hashSet.Add(refinements[r]);
												}
												else if ((cp1 == null && cp2 != null) || (cp2 == null && cp1 != null))
												{
													_warnings.Add($"Line with start: {cp1.X}, {cp1.Y}, {cp1.Z} and end: {cp2.X}, {cp2.Y}, {cp2.Z} is not valid: it must be on only one shape");
													continue;
												}
											}
										}
									}
									else if (refinements[r] is MeshPolylineRefinement poly)
									{
										GPC.Geometry.Polygon3d polygon = new GPC.Geometry.Polygon3d();
										bool check = true;
										foreach (Point3d pt in poly.Geometry)
										{
											if (surfaces[b].ClosestPoint(pt, out Point3d cp1, out _, out _, out _, rhinoEmbedTolerance, out _))
											{
												polygon.Add(new GPC.Geometry.Point3d(cp1.X, cp1.Y, cp1.Z));
											}
											else
											{
												check = false;
												_warnings.Add($"point {cp1.X}, {cp1.Y}, {cp1.Z} is not on plane or internal");
											}
										}
										if (check)
										{
											if (!geoms.Contains(polygon))
											{
												geoms.Add(polygon);
												embeddedGeometriesMeshSize.Add(polygon, poly.Size);
												hashSet.Add(refinements[r]);
											}
											else
											{
												_warnings.Add($"Fail to embed the polygon");
												continue;
											}
										}
									}
								}
							}
						}

						#endregion

						if (geoms.Count > 0)
						{
							embeddedGeometries.Add(shape, geoms.ToArray());
						}

						geoms.Clear();
					}
				}

				if ((constraints.Count + refinements.Count) != (embeddedGeometries.Select(i => i.Value.Count()).Sum() - embCountToRemove))
					_warnings.Add($"Costrain embedded to mesh: {embeddedGeometries.Select(i => i.Value.Count()).Sum()} of {constraints.Count}. " +
									$"Refinement embedded to mesh {embeddedGeometriesMeshSize.Select(i => i.Value).Sum()} of {refinements.Count}. " +
									$"Failed to match the geometry with a surface. Try to increase the tolerance");

				if (shapes.Count() == 0)
				{
					_errors.Add("There is no valid surface to mesh");
				}

			}
			catch (Exception e)
			{
				//SentrySdk.CaptureException(e);
				MessageBox.Show(e.Message);
			}

			return (shapes, embeddedGeometries, embeddedGeometriesMeshSize);
		}

		protected List<GH_Mesh> PostProcessMesh(List<GPC.Geometry.Meshes.Mesh> meshes, ref List<string> _errors)
		{
			try
			{
				Task<Mesh>[] tasks = new Task<Mesh>[meshes.Count];

				for (int m = 0; m < meshes.Count; m++)
				{
					var mesh = meshes[m];
					tasks[m] = Task.Run(() => mesh.ConvertToRhinoMesh());
				}

				Task.WaitAll(tasks);

				return tasks.Select(i => new GH_Mesh(i.Result)).ToList();
			}
			catch (Exception e)
			{
				_errors.Add(e.Message);
				//SentrySdk.CaptureException(e);

				return null;
			}
		}

		protected SolveResults ComputeMesh(List<Brep> surfaces, List<IGH_GeometricGoo> constraints, List<IGH_GeometricGoo> refinements,
			GPC.Geometry.Meshes.GMesh.GMesh.GMeshGenerateOptions generateOptions, double rhinoEmbedTolerance, double calcTolerance, double rhinoMatchTolerance, double size)
		{
			try
			{
				DateTime dtStart = DateTime.Now;

				SolveResults results = new SolveResults();

				List<string> errors = new List<string>();
				List<string> warnings = new List<string>();

				(List<GPC.Geometry.Shape> shapes, Dictionary<GPC.Geometry.Shape, GPC.Geometry.GeometryBase[]> embeddedGeometries,
					Dictionary<GPC.Geometry.GeometryBase, double> embeddedGeometriesMeshSize) =
					PreProcessMesh(surfaces, constraints, refinements, rhinoEmbedTolerance, calcTolerance, rhinoMatchTolerance, size, ref errors, ref warnings);

				bool status = GPC.Geometry.Meshes.GMesh.GMesh.Generate(shapes, embeddedGeometries, embeddedGeometriesMeshSize, generateOptions,
					out List<GPC.Geometry.Meshes.Mesh> meshes, out GPC.Geometry.Meshes.GMesh.GMesh.GMeshGenerateMeshStatus meshStatus);

				if (status)
				{
					List<GH_Mesh> GHMeshes = PostProcessMesh(meshes, ref errors);
					results.Value = GHMeshes;
				}

				string notPrintWarning = "Failed to recover the embedded geometries nodes";
				for (int j = 0; j < meshStatus.Warnings.Count; j++)
					if (meshStatus.Warnings[j] != notPrintWarning)
						warnings.Add(meshStatus.Warnings[j]);

				errors.AddRange(meshStatus.CustomErrorMessages.Select(i => i));

				results.Errors = errors;
				results.Warnings = warnings;
				results.Exceptions = meshStatus.Exceptions;
				results.TotalTime = (DateTime.Now - dtStart).TotalSeconds;

				return results;
			}
			catch (Exception e)
			{
				SolveResults results = new SolveResults
				{
					Exceptions = new List<Exception>() { e },
					Errors = new List<string>() { e.Message },
					Value = new List<GH_Mesh>()
				};

				//SentrySdk.CaptureException(e);

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
				#region Gestione fattore di scala & Opzioni mesh

				tolerance /= 1000;

				_doc = Rhino.RhinoDoc.ActiveDoc;
				if (_doc.ModelUnitSystem == Rhino.UnitSystem.Meters)
				{
					tolerance /= 1000;
				}

				double occTolerance = 1E-7;
				double scaleGeometryFactor = occTolerance / tolerance;                                      // occTolerance/tolerance;
				double calcTolerance = tolerance * scaleGeometryFactor;
				double scaleMeshFactor = 1 / scaleGeometryFactor;
				double sizeScale = size * scaleGeometryFactor;
				double matchMeshTolerance = tolerance * scaleGeometryFactor * 100;
				double matchGeomAndMesh = tolerance * scaleGeometryFactor / 10;
				double rhinoEmbedTolerance = 1E-4;
				double rhinoMatchTolerance = tolerance * 1000;

				// tolleranza di OpenCascade: 10E-7,
				// tolleranza di Gmsh: 10E-8,
				// tolleranza di GPCGeometry 10E-4
				// la tolleranza di gmsh possiamo cambiarla attraverso la funzione Geometry.Tolerance presente nel meshatore tra le opzioni del GenerateMeshOptions
				// usiamo questo metodo dello scalare < 1 la geometria per poter gestire la tolleranza di OCC. riducendo il modello, "aumentiamo" la tolleranza di OCC. 
				// questo ci permette di unifromare il comportamento di tutto il codice e non avere problemi di match di punti.
				// la tolleranza base è quella di OCC. l'utente, inserendo una tolleranza, scala il modello di una potenza tale da matchare la tollernza di imput e quella di OCC. 
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
				if (InPreSolve)
				{
					Task<SolveResults> task = Task.Run(() => ComputeMesh(surfaces, constraints, refinements, generateOptions, rhinoEmbedTolerance,
						calcTolerance, rhinoMatchTolerance, size), CancelToken);
					TaskList.Add(task);
					return;
				}

				// risultati non disponibili, vengono calcolati direttamente senza task
				if (!GetSolveResults(data, out result))
				{
					result = ComputeMesh(surfaces, constraints, refinements, generateOptions, rhinoEmbedTolerance, calcTolerance, rhinoMatchTolerance, size);
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

					data.SetDataList(0, result.Value);
				}

				_run = false;
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