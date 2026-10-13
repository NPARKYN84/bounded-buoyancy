// B14: foils (keels, rudders, sails) and propellers, run through the core's Appendages with the test integrator.
using System;
using System.Collections.Generic;
using BoundedBuoyancy.Core;

namespace BoundedBuoyancy.Checks
{
    static partial class Program
    {
        // Boats here point along -z (forward), +x to starboard, as in Godot; the chord of a foil points aft (+z).
        static readonly V3 Aft = new V3(0, 0, 1);
        static readonly V3 Fore = new V3(0, 0, -1);

        /// <summary>A small open motorboat hull: 2.4 m long, 0.9 m wide, 0.4 m deep, with a pointed bow.</summary>
        static BuoyancyShape3D MotorboatHull()
        {
            return Hull(-0.45, 0.2, -1.1, 0.45, 0.2, -1.1, -0.3, -0.2, -0.9, 0.3, -0.2, -0.9,
                -0.45, 0.2, 1.2, 0.45, 0.2, 1.2, -0.3, -0.2, 1.1, 0.3, -0.2, 1.1, 0.0, 0.2, -1.6, 0.0, -0.1, -1.4);
        }

        /// <summary>A motorboat: hull, keel, rudder and propeller, ballasted low. Returns the body; the rudder and
        /// propeller are its second foil and its thruster.</summary>
        static SimBody Motorboat(SimWorld w, V3 at, double yawDegrees = 0.0, bool keel = true, bool rudder = true, bool prop = true)
        {
            BuoyancyShape3D hull = MotorboatHull();
            var b = Body(w, hull, 250.0, at, 0.0, true, 0.0, 0.0, hull.Centroid + new V3(0, -0.12, 0));
            if (yawDegrees != 0.0)
            {
                b.Rot = Quat.AboutY(yawDegrees);
                b.Com = at + b.Basis.Mul(b.ComLocal);
            }
            if (keel)
                b.Foils.Add((new Foil3D { Area = 0.15, AspectRatio = 1.5 }, new V3(0, -0.4, 0), V3.Up, Aft));
            if (rudder)
                b.Foils.Add((new Foil3D { Area = 0.06, AspectRatio = 2.0, EffortOffset = 0.04 }, new V3(0, -0.3, 1.15), V3.Up, Aft));
            if (prop)
                b.Thrusters.Add((new Thruster3D { MaxThrust = 400.0, MaxSpeed = 5.0, Diameter = 0.25 }, new V3(0, -0.3, 1.1), Fore));
            return b;
        }

        static V3 Forward(SimBody b) { return b.Basis.Mul(Fore); }

        /// <summary>Signed angle (degrees) from a to b about +y: positive turns left (anticlockwise seen from above).</summary>
        static double YawBetween(V3 a, V3 b)
        {
            return Math.Atan2(a.Cross(b).y, a.x * b.x + a.z * b.z) * 180.0 / Math.PI;
        }

        /// <summary>Rudder angle (degrees) that steers `b` towards the horizontal direction `course`: a positive angle
        /// swings the trailing edge to starboard, which turns the boat to starboard (clockwise).</summary>
        static double Autopilot(SimBody b, V3 course, double gain = 1.5, double damping = 25.0, double most = 30.0)
        {
            double error = YawBetween(Forward(b), course);   // positive: the course is to port
            double a = -gain * error + damping * b.W.y * 180.0 / Math.PI / 60.0;
            return Math.Max(-most, Math.Min(most, a));
        }

        static void Boats()
        {
            Console.WriteLine("B14 Foils and propellers (keels, rudders, sails, propellers)");
            double rho = 1000.0, dt = 1.0 / 60.0;

            // A foil on a body too heavy to move, carried through still water at 2 m/s: the force square to its face
            // is ½ ρ A q² C_N(α) at every angle.
            {
                double k = Foil3D.LiftSlopeFor(2.0);
                Check("lift slope of a long wing (AR 10^6) / 2π", Foil3D.LiftSlopeFor(1e6) / (2.0 * Math.PI), 1.0, 1e-5);
                Check("lift slope of a short wing (AR 0.001) / (π AR / 2)", Foil3D.LiftSlopeFor(1e-3) / (Math.PI * 1e-3 / 2.0), 1.0, 1e-5);
                foreach (double deg in new[] { 5.0, 10.0, 30.0, 90.0 })
                {
                    var foil = new Foil3D { Area = 0.2, AspectRatio = 2.0, Pivot = V3.Zero, Span = V3.Up, RestChord = Aft };
                    double a = deg * Math.PI / 180.0;
                    // The foil moves forward and to port, so the water meets it at deg from the bow, coming from port.
                    V3 v = new V3(-Math.Sin(a), 0, -Math.Cos(a)) * 2.0;
                    V3 w = V3.Zero;
                    Appendages.Apply(new[] { foil }, null, 1e12, new V3(0, 1, 0), M3.Identity, new V3(1e12, 1e12, 1e12), ref v, ref w,
                        WaterSample.Flat(10.0, rho), V3.Zero, 0.0, dt);
                    double want = 0.5 * rho * 0.2 * 4.0 * Foil3D.NormalCoefficient(a, k);
                    Check(string.Format("plate at {0} deg: force square to it (N)", deg), foil.Force.x, want, 1e-6 * want + 1e-6);
                    Check(string.Format("plate at {0} deg: angle of attack (deg)", deg), Math.Abs(foil.AngleOfAttack), deg % 180.0 == 90.0 ? 90.0 : deg, 1e-9);
                }
                var dry = new Foil3D { Area = 0.2, Pivot = V3.Zero, Span = V3.Up, RestChord = Aft };
                V3 vd = new V3(-1, 0, -1);
                V3 wd = V3.Zero;
                Appendages.Apply(new[] { dry }, null, 1e12, V3.Zero, M3.Identity, new V3(1e12, 1e12, 1e12), ref vd, ref wd,
                    WaterSample.Flat(-5.0, rho), V3.Zero, 1.2, dt);
                Check("keel out of the water: no force (N)", dry.Force.Length, 0.0, 0.0);
            }

            // Bounded: a light body (1 kg) with a huge foil (10 m²) moving across it at 10 m/s. Applied explicitly, the
            // first step would throw it back at thousands of m/s; here the flow across it is stopped, never reversed.
            {
                double cn = 0.5 * rho * 10.0 * Foil3D.SquareOnCoefficient * 10.0;
                double explicitAfter = 10.0 - cn * dt / 1.0 * 10.0;
                var foil = new Foil3D { Area = 10.0, Pivot = V3.Zero, Span = V3.Up, RestChord = Aft };
                V3 v = new V3(10, 0, 0);
                V3 w = V3.Zero;
                Appendages.Apply(new[] { foil }, null, 1.0, V3.Zero, M3.Identity, new V3(0.1, 0.1, 0.1), ref v, ref w, WaterSample.Flat(10.0, rho), V3.Zero, 0.0, dt);
                Console.WriteLine("  (applied explicitly the first step would leave {0:F0} m/s)", explicitAfter);
                Check("1 kg body, 10 m² foil, 10 m/s across: speed after a step (m/s)", v.x, 10.0 / (1.0 + cn * dt), 1e-9);
                // Five such foils at the same point, and one 1 m below the centre of mass, together.
                var many = new List<Foil3D>();
                for (int i = 0; i < 5; i++)
                    many.Add(new Foil3D { Area = 10.0, Pivot = V3.Zero, Span = V3.Up, RestChord = Aft });
                many.Add(new Foil3D { Area = 10.0, Pivot = new V3(0, -1, 0), Span = V3.Up, RestChord = Aft });
                double worst = 0.0;
                v = new V3(10, 0, 0);
                w = new V3(0, 0, 3);
                for (int step = 0; step < 60; step++)
                {
                    Appendages.Apply(many, null, 1.0, V3.Zero, M3.Identity, new V3(0.1, 0.1, 0.1), ref v, ref w, WaterSample.Flat(10.0, rho), V3.Zero, 0.0, dt);
                    worst = Math.Max(worst, Math.Max(v.Length, w.Length));
                }
                Console.WriteLine("  six huge foils together, 60 steps: fastest speed or spin seen after the start {0:F3}", worst);
                Check("six huge foils together: never faster than at the start (10)", worst <= 10.0 ? 1 : 0, 1, 0);
            }

            // A keel: a boat going ahead at 3 m/s and slipping sideways at 0.5 m/s (leeway, 9.5 deg) loses the slip
            // within a second or so, and keeps most of its speed ahead; without the keel the slip lasts.
            {
                var w = new SimWorld();
                Water(w, new V3(0, 0, -900), new V3(100, 6, 100));
                var keeled = Motorboat(w, new V3(-20, 0.0, -880), 0.0, true, false, false);
                var bare = Motorboat(w, new V3(20, 0.0, -880), 0.0, false, false, false);
                Run(w, 3.0);
                keeled.V = new V3(0.5, 0, -3);
                bare.V = new V3(0.5, 0, -3);
                keeled.W = V3.Zero;
                bare.W = V3.Zero;
                Run(w, 1.0);
                // Sideways and ahead in each boat's own frame.
                double slipK = keeled.Basis.MulT(keeled.V).x, aheadK = -keeled.Basis.MulT(keeled.V).z;
                double slipB = bare.Basis.MulT(bare.V).x, aheadB = -bare.Basis.MulT(bare.V).z;
                Console.WriteLine("  after 1 s: with a keel {0:F3} m/s sideways, {1:F2} ahead; without {2:F3} sideways, {3:F2} ahead", slipK, aheadK, slipB, aheadB);
                Check("keel: sideways slip after 1 s, of the slip without one", Math.Abs(slipK) / Math.Abs(slipB), 0.0, 0.3);
                Check("keel: speed ahead kept, of the speed without one", aheadK / aheadB, 1.0, 0.15);
            }

            // Directional stability: a fin behind the centre of mass straightens a boat's course; ahead of it, the
            // boat turns round.
            {
                var w = new SimWorld();
                Water(w, new V3(0, 0, -1200), new V3(200, 6, 200));
                var behind = Motorboat(w, new V3(-20, 0, -1200), 0.0, false, false, false);
                behind.Foils.Add((new Foil3D { Area = 0.15, AspectRatio = 1.5 }, new V3(0, -0.4, 0.9), V3.Up, Aft));
                var ahead = Motorboat(w, new V3(20, 0, -1200), 0.0, false, false, false);
                ahead.Foils.Add((new Foil3D { Area = 0.15, AspectRatio = 1.5 }, new V3(0, -0.4, -0.9), V3.Up, Aft));
                Run(w, 3.0);
                foreach (var b in new[] { behind, ahead })
                {
                    b.V = new V3(0, 0, -3);
                    b.W = new V3(0, 0.4, 0);
                }
                Run(w, 6.0);
                Check("fin behind the centre of mass: spin after 6 s (deg/s)", behind.W.y * 180.0 / Math.PI, 0.0, 2.0);
                Check("fin behind: course turned by no more than (deg)", Math.Abs(YawBetween(Fore, Forward(behind))), 15.0, 15.0);
                Check("fin ahead of it: boat turned round, more than 60 deg", Math.Abs(YawBetween(Fore, Forward(ahead))) > 60.0 ? 1 : 0, 1, 0);
            }

            // A propeller gives a top speed of its own: with nothing else in the way (no drag, and no momentum shared
            // with the water as the hull bobs), throttle x MaxSpeed exactly, ahead and astern;
            // a huge one on a light body never overshoots it; out of the water it pushes nothing.
            {
                var w = new SimWorld();
                Water(w, new V3(0, 0, -1500), new V3(200, 6, 4000));
                var full = Motorboat(w, new V3(-30, 0, -1500), 0.0, false, false, true);
                var half = Motorboat(w, new V3(0, 0, -1500), 0.0, false, false, true);
                var astern = Motorboat(w, new V3(30, 0, -1500), 0.0, false, false, true);
                double fastest = 0.0;
                foreach (var b in new[] { full, half, astern })
                {
                    b.Core.DragCoefficient = 0.0;
                    b.Core.KeepMomentumOnEntry = false;
                }
                full.Thrusters[0].thruster.Throttle = 1.0;
                half.Thrusters[0].thruster.Throttle = 0.5;
                astern.Thrusters[0].thruster.Throttle = -1.0;
                Run(w, 60.0, t => fastest = Math.Max(fastest, -full.V.z));
                Check("nothing in the way, full throttle: top speed (m/s)", -full.V.z, 5.0, 0.02);
                // (The hull trims a little by the stern, so the propeller pushes a fraction of a degree off level and
                // the hull's own speed differs from the propeller's speed along its axis by a few mm/s.)
                Check("no overshoot on the way up: fastest seen (m/s)", fastest, 5.0, 0.02);
                Check("nothing in the way, half throttle: top speed (m/s)", -half.V.z, 2.5, 0.02);
                Check("nothing in the way, full astern: top speed (m/s)", astern.V.z, 5.0, 0.02);

                var t = new Thruster3D { MaxThrust = 1e7, MaxSpeed = 5.0, Throttle = 1.0, Position = V3.Zero, Direction = Fore };
                V3 v = V3.Zero, wv = V3.Zero;
                Appendages.Apply(null, new[] { t }, 1.0, V3.Zero, M3.Identity, new V3(1, 1, 1), ref v, ref wv, WaterSample.Flat(10.0, rho), V3.Zero, 0.0, dt);
                Check("10 MN propeller on 1 kg: speed after one step, at most (m/s)", -v.z, 5.0, 1e-3);
                var lifted = new Thruster3D { MaxThrust = 400.0, MaxSpeed = 5.0, Throttle = 1.0, Diameter = 0.25, Position = new V3(0, 1, 0), Direction = Fore };
                v = V3.Zero;
                wv = V3.Zero;
                Appendages.Apply(null, new[] { lifted }, 100.0, V3.Zero, M3.Identity, new V3(1, 1, 1), ref v, ref wv, WaterSample.Flat(0.0, rho), V3.Zero, 1.2, dt);
                Check("propeller lifted out of the water: thrust (N)", lifted.Force.Length, 0.0, 0.0);

                // With the hull's drag, the motorboat settles where thrust = drag, below the propeller's top speed.
                var w2 = new SimWorld();
                Water(w2, new V3(0, 0, -1800), new V3(400, 6, 60));
                var boat = Motorboat(w2, new V3(-150, 0, -1800), -90.0);
                boat.Thrusters[0].thruster.Throttle = 1.0;
                Run(w2, 40.0);
                double speed = boat.V.Length;
                double thrust = boat.Thrusters[0].thruster.Force.Length;
                Check("motorboat at full throttle: speed (m/s), below 5", speed < 5.0 && speed > 2.0 ? 1 : 0, 1, 0);
                Check("motorboat: thrust left at that speed = 400 (1 - v/5) (N)", thrust, 400.0 * (1.0 - speed / 5.0), 5.0);
                Check("motorboat runs straight with the rudder centred (deg/s)", boat.W.y * 180.0 / Math.PI, 0.0, 0.5);
            }

            // The rudder: hard over either way, the motorboat turns in a steady circle, to starboard for a positive
            // angle; the two circles are mirror images.
            {
                var w = new SimWorld();
                Water(w, new V3(0, 0, -2400), new V3(400, 6, 400));
                var right = Motorboat(w, new V3(-60, 0, -2400));
                var left = Motorboat(w, new V3(60, 0, -2400));
                right.Foils[1].foil.Angle = 25.0;
                left.Foils[1].foil.Angle = -25.0;
                right.Thrusters[0].thruster.Throttle = 1.0;
                left.Thrusters[0].thruster.Throttle = 1.0;
                Run(w, 30.0);
                double r1 = right.W.y;
                Run(w, 5.0);
                double r2 = right.W.y;
                double radius = right.V.Length / Math.Abs(right.W.y);
                Check("rudder +25 deg: turns to starboard (clockwise, deg/s < 0)", right.W.y < 0.0 ? 1 : 0, 1, 0);
                Check("rudder -25 deg: the mirror image (yaw rate ratio)", left.W.y / -right.W.y, 1.0, 0.01);
                Check("steady turn: yaw rate change over 5 s (%)", 100.0 * Math.Abs(r2 - r1) / Math.Abs(r1), 0.0, 3.0);
                Check("turning circle radius, boat lengths (2.4 m)", radius / 2.4, 3.5, 3.0);
                Check("turning boat stays upright (deg)", right.TiltDegrees, 0.0, 20.0);
            }

            // A keeled boat dropped at rest into a river swings its bow upstream while the water is still flowing past
            // it (as a moored boat does); once it drifts at the water's speed nothing flows past it, and it turns no
            // more.
            {
                var w = new SimWorld();
                var river = Water(w, new V3(0, 0, -3000), new V3(600, 6, 40));
                river.Current = new V3(1.5, 0, 0);
                var drifter = Motorboat(w, new V3(-200, 0, -3000), 30.0, true, true, false);
                Run(w, 40.0);
                double at40 = YawBetween(Fore, Forward(drifter));
                Run(w, 20.0);
                double at60 = YawBetween(Fore, Forward(drifter));
                Console.WriteLine("  keeled boat dropped into a river pointing 30 deg off the flow: points {0:F1} deg off after 40 s", at40);
                Check("keeled boat in a 1.5 m/s river: drifts with it (m/s)", drifter.V.x, 1.5, 0.05);
                Check("swung its bow towards upstream (-x, 90 deg)", at40 > 30.0 && at40 <= 90.0 ? 1 : 0, 1, 0);
                Check("drifting with the water, it turns no more (deg in 20 s)", at60 - at40, 0.0, 0.5);
            }

            // Sailing: a dinghy with a centreboard, a rudder and a sail that trims itself, steered by an autopilot,
            // makes ground towards the wind 45 deg off it, and runs before it, as real dinghies do.
            {
                foreach (var (label, offWind, minVmg) in new[] { ("close-hauled, 45 deg off the wind", 45.0, 0.6), ("beam reach, 90 deg", 90.0, -0.3), ("running before it, 180 deg", 180.0, -9.0) })
                {
                    var w = new SimWorld();
                    Water(w, new V3(0, 0, -5000), new V3(1000, 8, 1000));
                    var wind = new WindField { Velocity = new V3(0, 0, 6), GustStrength = 0.0, DirectionSwingDegrees = 0.0 };   // blowing towards +z
                    w.Winds.Add(wind);
                    // The wind comes from -z, so a course offWind degrees off it is offWind from -z.
                    V3 course = new V3(Math.Sin(offWind * Math.PI / 180.0), 0, -Math.Cos(offWind * Math.PI / 180.0));
                    var boat = Dinghy(w, new V3(0, 0, -5000), -offWind);
                    double gain = 0.0;
                    double heel = 0.0;
                    V3 start = V3.Zero;
                    double t = 0.0;
                    for (int i = 0; i < 60 * 90; i++)
                    {
                        boat.Foils[1].foil.Angle = Autopilot(boat, course);
                        w.Step();
                        t += w.Dt;
                        if (Math.Abs(t - 30.0) < 1e-6)
                            start = boat.Com;
                        if (t > 30.0)
                            heel = Math.Max(heel, boat.TiltDegrees);
                    }
                    double vmg = -(boat.Com - start).z / 60.0;   // towards the wind (-z), averaged over the last minute
                    double speed = (boat.Com - start).Length / 60.0;
                    Console.WriteLine("  dinghy {0}: {1:F2} m/s through the water, {2:F2} m/s towards the wind, heeling up to {3:F1} deg", label, speed, vmg, heel);
                    Check("dinghy " + label + ": speed (m/s)", speed > 1.0 ? 1 : 0, 1, 0);
                    Check("dinghy " + label + ": towards the wind, at least (m/s)", vmg >= minVmg ? 1 : 0, 1, 0);
                    Check("dinghy " + label + ": stays upright (deg)", heel, 12.0, 18.0);
                    Check("dinghy " + label + ": holds its course (deg)", Math.Abs(YawBetween(Forward(boat), course)), 0.0, 8.0);
                }
            }
        }

        /// <summary>A sailing dinghy, 3.6 m long, pointing `yawDegrees` (anticlockwise) from -z: a ballasted hull,
        /// a centreboard, a rudder and an 8 m² sail on a 4 m mast that trims itself.</summary>
        static SimBody Dinghy(SimWorld w, V3 at, double yawDegrees)
        {
            BuoyancyShape3D hull = Hull(-0.7, 0.25, -1.4, 0.7, 0.25, -1.4, -0.45, -0.15, -1.2, 0.45, -0.15, -1.2,
                -0.7, 0.25, 1.7, 0.7, 0.25, 1.7, -0.45, -0.15, 1.6, 0.45, -0.15, 1.6, 0.0, 0.25, -1.9, 0.0, -0.05, -1.7);
            var b = Body(w, hull, 160.0, at, 0.0, true, 1.2, 0.0, hull.Centroid + new V3(0, -0.45, 0));
            b.Rot = Quat.AboutY(yawDegrees);
            b.Com = at + b.Basis.Mul(b.ComLocal);
            b.Foils.Add((new Foil3D { Area = 0.5, AspectRatio = 2.0 }, new V3(0, -0.6, -0.1), V3.Up, Aft));
            b.Foils.Add((new Foil3D { Area = 0.12, AspectRatio = 2.5, EffortOffset = 0.06 }, new V3(0, -0.35, 1.65), V3.Up, Aft));
            b.Foils.Add((new Foil3D { Medium = Medium.Air, Area = 8.0, AspectRatio = 3.0, AutoTrim = true, EffortOffset = 0.55 }, new V3(0, 2.1, -0.6), V3.Up, Aft));
            return b;
        }
    }
}
