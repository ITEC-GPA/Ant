using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
//using Sentry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Ant.GrasshopperPlugIn.Components
{
	public class CleanGeometryComponent : GH_TaskCapableComponent<CleanGeometryComponent.SolveResults>
	{
		public class SolveResults
		{
			public List<Brep> Breps { get; set; }
			public List<GeometryBase> Geometries { get; set; }
			public string Log { get; set; }
			public List<string> Errors { get; set; }
			public List<string> Warnings { get; set; }
			public List<Exception> Exceptions { get; set; }
			public double TotalTime { get; set; }
		}


		public CleanGeometryComponent()
			: base("CleanGeometry", "CG", "Clean and match input geometries", "Ant", "Mesh")
		{

		}

		protected override void RegisterInputParams(GH_InputParamManager pManager)
		{
			pManager.AddBrepParameter("Surfaces", "S", "List of surfaces to clean. Surfaces must be planar with a tolerance of 1E-4", GH_ParamAccess.list);
			pManager.AddGeometryParameter("Embedded", "E", "List of embedded geometries", GH_ParamAccess.list); pManager[1].Optional = true;
			pManager.AddNumberParameter("Tolerance", "T", "The geometry tolerance. It don't affect flatness of the surfaces but only the edges and vertices matching. " +
				"Maximum admitted value: 100. " +
				"Is recommended to use values ​​less or equals than 1 in case of embedded geometries", GH_ParamAccess.item, 0.1);
		}

		protected override void RegisterOutputParams(GH_OutputParamManager pManager)
		{
			pManager.AddBrepParameter("Surfaces", "S", "The resulting surfaces", GH_ParamAccess.list);
			pManager.AddGeometryParameter("Embedded", "E", "List of embedded geometries", GH_ParamAccess.list);
			pManager.AddTextParameter("Info", "I", "Log of the process", GH_ParamAccess.list);
		}

		protected override void BeforeSolveInstance()
		{
			base.BeforeSolveInstance();
		}

		protected (List<Brep> breps, List<GeometryBase> geometries) ProcessGeometries(List<Brep> surfaces, List<IGH_GeometricGoo> geometry,
			double tolerance, ref List<string> _errors, ref List<string> _warnings)
		{
			HashSet<IGH_GeometricGoo> hashSet = new HashSet<IGH_GeometricGoo>();
			HashSet<Point> hashPoint = new HashSet<Point>();
			List<Brep> outSurfaces = new List<Brep>();
			List<GeometryBase> outGeometry = new List<GeometryBase>();

			try
			{
				int embCountToRemove = 0;

				for (int b = 0; b < surfaces.Count; b++)
				{
					if (surfaces[b] == null)
						continue;

					List<Curve> loops = new List<Curve>();

					for (int loop = 0; loop < surfaces[b].Loops.Count; loop++)
					{
						#region Creazione della Shape

						List<Point> polygon = new List<Point>();

						Curve curve = surfaces[b].Loops[loop].To3dCurve();

						if (curve.Degree > 1)
						{
							PolylineCurve polylineCurve = null;
							try
							{
								polylineCurve = ToPolylineCurve(curve, tolerance);
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
										Point point = new Point(polylineCurve.Point(j));
										if (PointExist(hashPoint, point, tolerance, out Point matchStart))
											point = matchStart;

										if (!PointExist(polygon, point, tolerance, out Point pmtc))
										{
											polygon.Add(point);
											hashPoint.Add(point);
										}
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
									Point point = new Point(pline[p]);
									if (PointExist(hashPoint, point, tolerance, out Point matchStart))
										point = matchStart;

									if (!PointExist(polygon, point, tolerance, out Point pmtc))
									{
										polygon.Add(point);
										hashPoint.Add(point);
									}
								}
								catch (ArgumentException)
								{
									_errors.Add($"Polygon with additional point {pline[p]} is not planar");
									continue;
								}
							}
						}

						if (polygon.Count() > 2)
						{
							List<Point3d> ppts = polygon.Select(i => i.Location).ToList();
							ppts.Add(polygon.First().Location);
							PolylineCurve polylineCurve = new PolylineCurve(ppts);
							loops.Add(polylineCurve);
						}

						#endregion
					}

					Brep[] breps = Brep.CreatePlanarBreps(loops, 0.1);

					if (breps != null)
						outSurfaces.AddRange(breps);

					if (loops.Count > 0)
					{
						List<GeometryBase> geoms = new List<GeometryBase>();

						#region Embedded

						if (geometry != null)
						{
							for (int ct = 0; ct < geometry.Count; ct++)
							{
								if (geometry[ct] == null)
									continue;

								bool embed = false;

								if (!hashSet.Contains(geometry[ct]))
								{
									if (geometry[ct] is GH_Point point && embed == false)
									{
										if (surfaces[b].ClosestPoint(point.Value, out Point3d closest, out _, out _, out _, tolerance, out _))
										{
											Point p = new Point(closest);
											if (!PointExist(hashPoint, p, tolerance, out Point match))
											{
												hashPoint.Add(p);
												geoms.Add(p);
												hashSet.Add(geometry[ct]);
												embed = true;
											}
										}
									}
									else if (geometry[ct] is GH_Line line && embed == false)
									{
										if (surfaces[b].ClosestPoint(line.Value.From, out Point3d cp1, out _, out _, out _, tolerance, out _) &&
											surfaces[b].ClosestPoint(line.Value.To, out Point3d cp2, out _, out _, out _, tolerance, out _))
										{
											if (cp1 != null && cp2 != null)
											{
												if (cp1.DistanceTo(cp2) > tolerance)
												{
													Point start = new Point(new Point3d(cp1.X, cp1.Y, cp1.Z));
													Point end = new Point(new Point3d(cp2.X, cp2.Y, cp2.Z));

													if (PointExist(hashPoint, start, tolerance, out Point matchStart))
														start = matchStart;

													if (PointExist(hashPoint, end, tolerance, out Point matchEnd))
														end = matchEnd;

													LineCurve l = new LineCurve(start.Location, end.Location);
													geoms.Add(l);
													hashSet.Add(geometry[ct]);
													hashPoint.Add(start);
													hashPoint.Add(end);
													embed = true;
												}
											}
											else if ((cp1 == null && cp2 != null) || (cp2 == null && cp1 != null))
											{
												_warnings.Add($"Line with start: {cp1.X}, {cp1.Y}, {cp1.Z} and end: {cp2.X}, {cp2.Y}, {cp2.Z} is not valid: it must be on only one shape");
												continue;
											}
										}
									}
									else if (geometry[ct] is GH_Curve curve && embed == false)
									{
										if (curve.Value is LineCurve lc && embed == false)
										{
											if (surfaces[b].ClosestPoint(lc.PointAtStart, out Point3d cp1, out _, out _, out _, tolerance, out _) &&
												surfaces[b].ClosestPoint(lc.PointAtEnd, out Point3d cp2, out _, out _, out _, tolerance, out _))
											{
												if (cp1 != null && cp2 != null)
												{
													if (cp1.DistanceTo(cp2) > tolerance)
													{
														Point start = new Point(new Point3d(cp1.X, cp1.Y, cp1.Z));
														Point end = new Point(new Point3d(cp2.X, cp2.Y, cp2.Z));

														if (PointExist(hashPoint, start, tolerance, out Point matchStart))
															start = matchStart;

														if (PointExist(hashPoint, end, tolerance, out Point matchEnd))
															end = matchEnd;

														LineCurve l = new LineCurve(start.Location, end.Location);
														geoms.Add(l);
														hashSet.Add(geometry[ct]);
														hashPoint.Add(start);
														hashPoint.Add(end);
														embed = true;
													}
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

												if (surfaces[b].ClosestPoint(p1, out Point3d cp1, out _, out _, out _, tolerance, out _) &&
													surfaces[b].ClosestPoint(p2, out Point3d cp2, out _, out _, out _, tolerance, out _))
												{
													if (cp1 != null && cp2 != null)
													{
														if (cp1.DistanceTo(cp2) > tolerance)
														{
															Point start = new Point(new Point3d(cp1.X, cp1.Y, cp1.Z));
															Point end = new Point(new Point3d(cp2.X, cp2.Y, cp2.Z));

															if (PointExist(hashPoint, start, tolerance, out Point matchStart))
																start = matchStart;

															if (PointExist(hashPoint, end, tolerance, out Point matchEnd))
																end = matchEnd;

															LineCurve l = new LineCurve(start.Location, end.Location);
															geoms.Add(l);
															hashPoint.Add(start);
															hashPoint.Add(end);
															embed = true;

															if (i != 0)
																embCountToRemove++;
														}
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
												hashSet.Add(geometry[ct]);
										}
										else if (curve.Value is Curve c && embed == false)
										{
											bool add = true;
											PolylineCurve polylineCurve = ToPolylineCurve(c, tolerance);

											for (int i = 0; i < polylineCurve.PointCount - 1; i++)
											{
												Point3d p1 = polylineCurve.Point(i);
												Point3d p2 = polylineCurve.Point(i + 1);

												if (surfaces[b].ClosestPoint(p1, out Point3d cp1, out _, out _, out _, tolerance, out _) &&
													surfaces[b].ClosestPoint(p2, out Point3d cp2, out _, out _, out _, tolerance, out _))
												{
													if (cp1 != null && cp2 != null)
													{
														if (cp1.DistanceTo(cp2) > tolerance)
														{
															Point start = new Point(new Point3d(cp1.X, cp1.Y, cp1.Z));
															Point end = new Point(new Point3d(cp2.X, cp2.Y, cp2.Z));

															if (PointExist(hashPoint, start, tolerance, out Point matchStart))
																start = matchStart;

															if (PointExist(hashPoint, end, tolerance, out Point matchEnd))
																end = matchEnd;

															LineCurve l = new LineCurve(start.Location, end.Location);

															geoms.Add(l);
															embed = true;

															if (i != 0)
																embCountToRemove++;
														}
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
												hashSet.Add(geometry[ct]);
										}
									}
									else if (geometry[ct] is GH_Surface embSurf)
									{
										Point[] points = new Point[embSurf.Value.Vertices.Count];
										bool added = true;

										for (int i = 0; i < embSurf.Value.Vertices.Count; i++)
										{
											if (surfaces[b].ClosestPoint(embSurf.Value.Vertices[i].Location, out Point3d cp, out _, out _, out _, tolerance, out _))
											{
												Point pp = new Point(new Point3d(cp.X, cp.Y, cp.Z));

												if (PointExist(hashPoint, pp, tolerance, out Point matchPP))
													pp = matchPP;

												points[i] = pp;
												hashPoint.Add(points[i]);
											}

											else if (cp == null)
											{
												added = false;
												_warnings.Add($"Point: {cp.X}, {cp.Y}, {cp.Z} of surface {embSurf} is not valid: it must be on one shape");
												continue;
											}
										}
										if (added)
										{/*
											GPC.Geometry.Shape s = new GPC.Geometry.Shape(new GPC.Geometry.Polygon3d(points, tolerance));

											geoms.Add(s);
											hashSet.Add(geometry[ct]);
											embed = true;*/
										}
									}
									else if (geometry[ct] is GH_Brep brep)
									{
										Point[] points = new Point[brep.Value.Vertices.Count];
										bool added = true;

										for (int i = 0; i < brep.Value.Vertices.Count; i++)
										{
											bool addedBuffer = true;

											addedBuffer = surfaces[b].ClosestPoint(brep.Value.Vertices[i].Location, out Point3d cp, out _, out _, out _, tolerance, out _);

											if (addedBuffer)
											{
												points[i] = new Point(new Point3d(cp.X, cp.Y, cp.Z));
												hashPoint.Add(points[i]);
											}
											else
												added = false;
										}
										if (added)
										{/*
											GPC.Geometry.Shape s = new GPC.Geometry.Shape(new GPC.Geometry.Polygon3d(points, tolerance));

											geoms.Add(s);
											hashSet.Add(geometry[ct]);
											embed = true;*/
										}
									}
								}
							}
						}

						#endregion

						outGeometry.AddRange(geoms);

						geoms.Clear();
					}
				}


				if (surfaces.Count() == 0)
				{
					_errors.Add("There is no valid surface to mesh");
				}

			}
			catch (Exception e)
			{
				//SentrySdk.CaptureException(e);
				MessageBox.Show(e.Message);
			}

			return (outSurfaces, outGeometry);
		}

		protected SolveResults ComputeClean(List<Brep> surfaces, List<IGH_GeometricGoo> constraints, double tolerance)
		{
			try
			{
				DateTime dtStart = DateTime.Now;

				SolveResults results = new SolveResults();

				List<string> errors = new List<string>();
				List<string> warnings = new List<string>();
				List<Exception> exceptions = new List<Exception>();

				(List<Brep> breps, List<GeometryBase> geometries) geometries = ProcessGeometries(surfaces, constraints, tolerance, ref errors, ref warnings);

				results.Breps = geometries.breps;
				results.Geometries = geometries.geometries;

				results.Errors = errors;
				results.Warnings = warnings;
				results.Exceptions = exceptions;
				results.TotalTime = (DateTime.Now - dtStart).TotalSeconds;
				results.Log = $"Removed surfaces: {surfaces.Count - geometries.breps.Count}. \r\n" +
					$"Removed geometries: {constraints.Count - geometries.geometries.Count}";

				return results;
			}
			catch (Exception e)
			{
				SolveResults results = new SolveResults
				{
					Exceptions = new List<Exception>() { e },
					Errors = new List<string>() { e.Message },
					Breps = new List<Brep>(),
					Geometries = new List<GeometryBase>(),
					Log = "",
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

			if (!data.GetData(2, ref tolerance))
				return;

			#endregion

			SolveResults result = new SolveResults();

			// Componente prova ad usare i task, viene lanciato il thread e poi ritorna.
			// Poi il componente farà un altro passaggio per raccogliere i risultati
			if (InPreSolve)
			{
				Task<SolveResults> task = Task.Run(() => ComputeClean(surfaces, constraints, tolerance), CancelToken);
				TaskList.Add(task);
				return;
			}

			// risultati non disponibili, vengono calcolati direttamente senza task
			if (!GetSolveResults(data, out result))
			{
				result = ComputeClean(surfaces, constraints, tolerance);
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

				data.SetDataList(0, result.Breps);
				data.SetDataList(1, result.Geometries);
				data.SetData(2, result.Log);
			}
		}

		protected PolylineCurve ToPolylineCurve(Curve curve, double tolerance)
		{
			return curve.ToPolyline(0, 0, Math.PI / 16.0, 1.414, 0, 0, 10 * tolerance, 0, false);
		}

		protected bool PointExist(HashSet<Point> points, Point point, double tolerance, out Point pointMatched)
		{
			for (int i = 0; i < points.Count; i++)
			{
				if (points.ElementAt(i).Location.DistanceTo(point.Location) < tolerance)
				{
					pointMatched = points.ElementAt(i);
					return true;
				}
			}

			pointMatched = new Point(new Point3d());
			return false;
		}

		protected bool PointExist(List<Point> points, Point point, double tolerance, out Point pointMatched)
		{
			for (int i = 0; i < points.Count; i++)
			{
				if (points.ElementAt(i).Location.DistanceTo(point.Location) < tolerance)
				{
					pointMatched = points.ElementAt(i);
					return true;
				}
			}

			pointMatched = new Point(new Point3d());
			return false;
		}

		protected override System.Drawing.Bitmap Icon => Properties.Resources.CleanGeometryIcon;

		public override Guid ComponentGuid => new Guid("169774c8-75df-45fb-a79e-e8830a020c06");

		public override GH_Exposure Exposure => GH_Exposure.primary;
	}
}