# Fixed-point math reference

Namespace and assembly `Pragma.Dots.Mathematics`: part of the core, always compiled, no Entities dependency (it
references only Unity.Mathematics and Burst). The recipes below use lockstep types (`LockstepTime`,
`LockstepSessionInfo`).

| Type | Role | Float counterpart |
|---|---|---|
| `FixedPoint` | Scalar | `float` |
| `FixedVector2`, `FixedVector3`, `FixedVector4` | Vectors | `float2` / `Vector2`, `float3` / `Vector3`, `float4` / `Vector4` |
| `FixedQuaternion` | Rotation | `quaternion` / `Quaternion` |
| `FixedPose` | Position and rotation | `Pose` |
| `FixedMatrix3x3`, `FixedMatrix4x4` | Linear maps, transforms | `float3x3`, `float4x4` / `Matrix4x4` |
| `FixedMath` | Functions and constants | `Mathf`, `math` |
| `FixedRandom` | Random numbers | `Unity.Mathematics.Random` |
| `FixedGrid` | Cells of a grid on the XZ plane | |
| `FixedBounds2`, `FixedCircle`, `FixedSegment2`, `FixedRay2`, `FixedTriangle2` | Shapes on the XZ plane | `Rect`, `Physics2D` queries |
| `FixedCurve`, `FixedCurveKey` | Curves | `AnimationCurve`, `Keyframe` |
| `FixedHash` | Hash of state | |

## Contents

- `FixedPoint` format and limits
- Creating values
- Operators
- Overflow checks
- `FixedMath` functions
- Vectors
- Quaternions and poses
- Matrices
- `FixedRandom`
- `FixedGrid`
- 2D geometry
- `FixedCurve`
- `FixedHash`
- Inspector
- Serialization
- Recipes and pitfalls

## `FixedPoint` format and limits

- `long rawValue` with 16 fractional bits (Q47.16). `FixedPoint.ONE_RAW` = 65536 is 1.0,
  `FixedPoint.FRACTIONAL_BITS` = 16.
- Step 1/65536 (about 0.000015). Range about ±1.4e14 (`FixedPoint.MaxValue`, `FixedPoint.MinValue`;
  `MinValue = -MaxValue`, so negation never overflows).
- `a * b` uses a 64-bit intermediate: keep `|a * b|` below about 2.1e9. `FixedMath.MulShiftRound` and
  `FixedMath.MulDiv(a, b, c)` (`a * b / c`, truncated; saturates) use a 128-bit intermediate; division falls back to
  exact long division when the shifted dividend would overflow.
- A `FixedRandom` left at `default` runs the default sequence from a zero state: seed every generator.
- Every operation is integer-only: identical bits in Mono, IL2CPP and Burst on every platform.

## Creating values

| Expression | Result |
|---|---|
| `FixedPoint x = 3;` | Implicit from `int` |
| `(FixedPoint)someLong` | Explicit from `long` |
| `FixedPoint.FromFraction(1, 3)` | Exact fraction, truncated toward zero |
| `FixedPoint.FromRaw(32768)` | 0.5 from the raw value |
| `FixedPoint.Parse("2.75")`, `FixedPoint.TryParse(text, out value)` | Deterministic decimal parsing; `.` or `,`; no exponent |
| `(FixedPoint)2.75f`, `(FixedPoint)2.75` | Rounded to the nearest step. Deterministic for the same float bits, so fine for bake-time data and literals; never for floats computed during a tick |
| `FixedPoint.Zero`, `One`, `Half`, `Two`, `MinusOne`, `Epsilon` | Constants |

Tuning values as properties keep everything exact and Burst-friendly:

```csharp
public static class Tuning
{
    public static FixedPoint Speed => FixedPoint.FromFraction(7, 2);       // 3.5
    public static FixedPoint Gravity => -FixedPoint.FromFraction(981, 100); // -9.81
}
```

## Operators

| Operator | Semantics |
|---|---|
| `+`, `-`, unary `-` | Exact; wrap on overflow like `long` (throw with `PRAGMA_DOTS_CHECKED_MATH`) |
| `FixedPoint * FixedPoint` | Rounded to the nearest step, halves up |
| `FixedPoint * int`, `int * FixedPoint` | Exact |
| `FixedPoint / FixedPoint`, `FixedPoint / int` | Truncated toward zero; division by zero saturates to `MaxValue`/`MinValue` (or 0 for 0/0) instead of throwing |
| `%` | Sign of the dividend, like C#; zero divisor returns 0 |
| `==`, `!=`, `<`, `>`, `<=`, `>=`, `++`, `--` | As expected |
| `(int)x`, `(long)x` | Truncate toward zero |
| `(float)x`, `(double)x`, `ToString()` | Display only |

## Overflow checks

- `FixedPoint.TryAdd(a, b, out r)`, `TrySubtract`, `TryMultiply(a, b, out r)`, `TryMultiply(a, int, out r)`: `false`
  on overflow, and `r` is `MaxValue`/`MinValue` instead of a wrapped value; on success `r` is what the operator gives.
- The scripting define `PRAGMA_DOTS_CHECKED_MATH` (Player Settings or a development build profile; not a module, the
  settings page does not handle it) makes `+`, `-`, `*`, unary `-`, `++`, `--` and the `long` conversion throw
  `OverflowException` where they would wrap, in Burst too. Without it the checks are compiled out. Results are the same
  either way, so builds with and without it play together. Division and `MulDiv` saturate and never throw.
- Turn it on when a value comes out huge or with a flipped sign: the exception points at the product that overflowed.

## `FixedMath` functions

| Group | Functions |
|---|---|
| Basic | `Abs`, `Sign` (-1/0/1), `Min`, `Max`, `Clamp`, `Clamp01`, `Lerp`, `InverseLerp` (0 when a == b), `Remap`, `MulDiv(a, b, c)`, `Select(falseValue, trueValue, test)`, `Step`, `SmoothStep`, `MoveTowards` |
| Rounding | `Floor`, `Ceil`, `Round` (halves up), `Truncate`, `Frac`, `FloorToInt`, `CeilToInt`, `RoundToInt` |
| Modulo | `Remainder` (sign of the dividend, like `%`), `Mod` (in `[0, Abs(y))`), `Wrap(x, min, max)` |
| Powers | `Sqrt` (±1 step below about 2e9), `Rsqrt`, `Exp`, `Exp2`, `Log`, `Log2`, `Log10`, `Pow(FixedPoint, FixedPoint)`, `Pow(FixedPoint, int)` |
| Trigonometry | `Sin`, `Cos`, `SinCos`, `Tan`, `Asin`, `Acos`, `Atan`, `Atan2(y, x)` - radians |
| Angles | `ToRadians(degrees)`, `ToDegrees(radians)`, `DeltaAngle(from, to)` and `WrapAngle(angle)` in `[-Pi, Pi)`; `Yaw(rotation)`, `Yaw(direction)`, `Yaw(directionXz)`: the heading on the ground, radians from +Z toward +X (what `FixedQuaternion.RotateY` takes); bearing of a point = `DeltaAngle(Yaw(rotation), Yaw(offset.Xz))` |
| Constants | `Pi`, `TwoPi`, `HalfPi`, `E`, `Ln2`, `Ln10`, `Sqrt2`, `Rad2Deg`, `Deg2Rad`, `Epsilon` |
| Matrices | `Mul`, `Transpose`, `Determinant`, `Inverse`, `TryInverse`, `InverseAffine`, `TryInverseAffine`, `TransformPoint`, `TransformDirection`, `TryDecompose` |
| Geometry | `Orientation(a, b, c)` (1 left, -1 right, 0 on the line; exact), `PolygonContains(ReadOnlySpan<FixedVector2>, point)` (even-odd, exact) |
| Raw | `MulShiftRound(a, b, shift)`, `DivideRaw(a, b)`, `SqrtRounded(ulong)` |

Accuracy against `double`: `Sqrt` 1 step below about 2e9 (above, within 128 steps: it drops the 8 lowest bits); `Sin`, `Cos`, `Atan`, `Atan2` 2 steps; `Asin`, `Acos`, `Log*` 3 steps;
`Exp`, `Exp2`, `Tan` 0.01-0.02 %. Out-of-domain input saturates: `Sqrt(-1)` = 0, `Log(0)` = `FixedPoint.MinValue`,
`Asin`/`Acos` clamp to [-1, 1], `Tan` saturates where `Cos` is 0, `Exp` saturates above about 32.6.

## Vectors

`FixedVector2 { x, y }`, `FixedVector3 { x, y, z }`:

- Constructors `new FixedVector2(x, y)`, `new FixedVector2(value)`, `new FixedVector3(x, y, z)`,
  `new FixedVector3(xy, z)`.
- Constants `Zero`, `One`, `Up`, `Down`, `Left`, `Right` (+ `Forward`, `Back` for `FixedVector3`); swizzles `Yx`,
  `Xy`, `Xz`, `Yz`; indexer `[i]`.
- Operators: component-wise `+ - * /`, `vector * FixedPoint`, `FixedPoint * vector`, `/ FixedPoint`, `* int`,
  `/ int` (no `int * vector`), `==`.
- `FixedMath` for `FixedVector2`: `Dot`, `Cross` (z of the 3D cross product), `Length`, `LengthSquared`, `Distance`,
  `DistanceSquared`, `Normalize`, `NormalizeSafe(v, default)`, `ClampLength`, `MoveTowards`, `Rotate(v, angle)`,
  `Perpendicular`, `Reflect`, `Direction(angle)`, `Angle(v)`, `Abs`, `Min`, `Max`, `Clamp`, `Lerp`, `Floor`, `Round`.
- `FixedMath` for `FixedVector3`: `Dot`, `Cross`, `Length`, `LengthSquared`, `Distance`, `DistanceSquared`,
  `Normalize`, `NormalizeSafe`, `ClampLength`, `MoveTowards`, `Reflect`, `Project`, `ProjectOnPlane`, `Angle(a, b)`,
  `Abs`, `Min`, `Max`, `Clamp`, `Lerp`, `Floor`, `Round`.
- `FixedVector4 { x, y, z, w }`: the same operators, `new FixedVector4(xyz, w)`, swizzles `Xyz`, `Xy`; `FixedMath`
  `Dot`, `Length`, `LengthSquared` (overflows above about 23 000), `Distance`, `Normalize`, `NormalizeSafe`, `Abs`,
  `Min`, `Max`, `Clamp`, `Lerp`. A matrix column or a homogeneous point.
- Conversions: implicit from `int2`/`int3`/`int4` (not back: `FixedMath.FloorToInt` per component or
  `FixedGrid.WorldToCell`); `float2`, `float3`, `float4`, `Vector2`, `Vector3`, `Vector4` explicit both ways (display
  and bake time only).

## Quaternions and poses

`FixedQuaternion { x, y, z, w }`, laid out like `Unity.Mathematics.quaternion`:

- `Identity`, `AxisAngle(axis, angle)`, `RotateX/Y/Z(angle)`, `Euler(FixedVector3 radians)` (Unity's order: z, then
  x, then y), `LookRotation(forward, up)`, `FromBasis(right, up, forward)`, `FromMatrix(FixedMatrix3x3)` (scale
  dropped), `Xyz`.
- `q * v` rotates a vector; `a * b` applies `b` first (Hamilton product).
- `FixedMath`: `Mul`, `Rotate`, `Conjugate`, `Inverse`, `Dot`, `Length`, `Normalize`, `NormalizeSafe`, `Nlerp`,
  `Slerp`, `Angle(a, b)`, `RotateTowards(from, to, maxRadians)`, `Forward(q)`, `Up(q)`, `Right(q)`.
- Explicit conversions to and from `quaternion` and `Quaternion`. Normalize after many multiplications.
- `FixedPose { position, rotation }`: `Identity`, `Forward`, `Up`, `Right`, equality. The placement of actors
  (`IActorBuilder.Placement`); `LockstepTransform.FromPose(pose, scale)` turns it into a lockstep transform.

## Matrices

`FixedMatrix3x3 { c0, c1, c2 }` and `FixedMatrix4x4 { c0, c1, c2, c3 }`, stored by columns like `float3x3`/`float4x4`
and `Matrix4x4` (explicit conversions). `m * v` transforms a column vector; `a * b` applies `b` first, so
`parent * child`. The nine- and sixteen-element constructors take rows, as written on paper.

- Build: `Identity`, `Zero`, `FixedMatrix3x3.FromRotation(q)`, `FixedMatrix3x3.Scale(v)`,
  `FixedMatrix4x4.TRS(t, r, s)`, `FromPose(pose)`, `Translate`, `Rotate`, `Scale`,
  `new FixedMatrix4x4(linear, translation)`.
- Parts: `this[column]`, `Translation`, `Linear` (the 3x3 of a 4x4).
- Points and directions: `FixedMath.TransformPoint(m, p)`, `FixedMath.TransformDirection(m, d)` (affine; the bottom row
  is ignored).
- Inverses: `Transpose` is the inverse of a rotation (exact, cheap); `TryInverse`/`Inverse` of a 3x3 and
  `TryInverseAffine`/`InverseAffine` of a 4x4 are accurate to a step at any scale and translation (columns up to about
  1300 long); `TryInverse` of a non-affine 4x4 (a projection) to about 1e-5 of each element, elements up to about 100.
  A singular matrix gives `false` and a zero matrix. An inverse costs 64-bit long divisions: compute it when the
  transform changes, not per point.
- `FixedMath.TryDecompose(m, out t, out r, out s)`: a TRS back into its parts; a mirror is a negative x scale, shear is
  not represented, a zero scale gives `false`.

## `FixedRandom`

PCG32 (XSH-RR), 128 bits of state, seeded through SplitMix64. A struct: store it in a component and access it by
`ref`, or the state does not advance.

| Member | Result |
|---|---|
| `new FixedRandom(seed)`, `new FixedRandom(seed, sequence)` | Generator; different sequences are independent streams |
| `FixedRandom.CreateFromIndex(seed, index)` | Per-index stream, e.g. `(LockstepSessionInfo.seed, lockstepEntityId.value)` once the id is assigned |
| `FixedRandom.CreateFromKey(seed, key, index = 0)`, `FixedRandom.Key(name)` | A stream per system (and index): `CreateFromKey(info.seed, LootKey, id)` with `static readonly ulong LootKey = FixedRandom.Key("loot")`. Drawing more in one system then shifts no other system's numbers. `Key` is the same everywhere, unlike `string.GetHashCode` |
| `NextUInt()`, `NextUInt(max)`, `NextUInt(min, max)` | Unbiased |
| `NextInt(max)`, `NextInt(min, max)` | `[min, max)` |
| `NextBool()`, `NextChance(FixedPoint probability)` | |
| `NextFixedPoint()`, `NextFixedPoint(min, max)` | `[0, 1)`, `[min, max)` |
| `NextVector2(min, max)`, `NextVector3(min, max)` | Component-wise |
| `NextAngle()` | `[0, TwoPi)` |
| `NextDirection2()`, `NextDirection3()`, `NextInsideUnitCircle()`, `NextRotation()` | Uniform geometry |
| `FixedRandom.SplitMix64(value)` | Mixer for deriving seeds |

## `FixedGrid`

The cell math of the navigation and vision grids, usable for grids of your own: `new FixedGrid(origin, cellSize,
width, height)` (`origin` is the corner of cell 0, 0 on X and Z), `CellCount`, `IsValid`, `Contains(cell)`,
`GetIndex(cell)` (row by row along X), `GetCell(index)`, `WorldToCell(xz)` (integer math on raw values; may lie
outside), `GetCellMin`, `GetCellCenter`, `ClampToGrid`.

## 2D geometry

Shapes of `FixedVector2` on the ground: pass `position.Xz`. Edges belong to the shapes: a point on an edge is inside,
touching shapes overlap.

| Type | Fields | Members |
|---|---|---|
| `FixedBounds2` | `min`, `max` | `FromCenterSize`, `FromPoints`, `Center`, `Size`, `Extents`, `IsValid`, `Contains(point / bounds)`, `Overlaps(bounds / circle)`, `ClosestPoint`, `Encapsulate`, `Expand` |
| `FixedCircle` | `center`, `radius` | `Contains`, `Overlaps(circle / bounds)`, `ClosestPoint`, `Bounds`, `TrySweep(movement, target, out fraction)` |
| `FixedSegment2` | `start`, `end` | `PointAt(t)`, `ClosestPoint`, `ClosestParameter`, `Distance(point / segment)`, `Intersects` / `TryIntersect(segment, out point)`, `Intersects` / `TryIntersect(circle, out fraction)`, `Length`, `Delta`, `Bounds` |
| `FixedRay2` | `origin`, `direction` (normalized by the constructor) | `TryIntersect(circle / bounds / segment, out distance)`, `GetPoint(distance)` |
| `FixedTriangle2` | `a`, `b`, `c` | `Contains`, `SignedArea` (positive counter-clockwise), `Area`, `Centroid`, `Bounds` |

- Yes-or-no answers (containment, overlap, `Orientation`, segment intersection) are exact for any coordinates: they
  compare 128-bit products and never overflow. Prefer `circle.Contains(point)` to `DistanceSquared < r * r`, which
  overflows above about 46 000.
- Points and distances are accurate to a step (rays: about 2e-5 of the distance); circles in sweeps and ray tests take
  radii up to about 46 000. An origin or start inside a shape hits at 0.
- A capsule is a segment and a radius: `segment.Distance(point) <= radius`; two capsules overlap when
  `a.Distance(b) <= ra + rb`.
- `LockstepSpatialIndex` finds the candidates near a point; the shapes decide what is hit.

## `FixedCurve`

A curve of `FixedCurveKey { time, value, inTangent, outTangent, interpolation }` (`FixedCurveInterpolation.Cubic`,
`Linear`, `Constant`; tangents are slopes), with `FixedCurveWrap.Clamp`, `Loop` or `PingPong` before the first and
after the last key, evaluated like `AnimationCurve`. Times must strictly increase (the constructor throws).

- `FixedCurve.Constant(v)`, `Linear(t0, v0, t1, v1)`, `EaseInOut(t0, v0, t1, v1)`,
  `new FixedCurve(preWrap, postWrap, keys)`; `Evaluate(time)`: 0 for no keys, the value of a single key.
- A `[SerializeField] FixedCurve` field (an actor part's too) is edited with Unity's curve editor and stores
  fixed-point keys.
- `FixedCurve` is a class: never in a component. A part copies `curve.Keys` into a buffer whose element holds one
  `FixedCurveKey` (`builder.AddBuffer<T>()`; `IActorBuilder` has no blobs), and Burst code calls
  `FixedCurve.Evaluate(buffer.Reinterpret<FixedCurveKey>().AsNativeArray().AsReadOnlySpan(), preWrap, postWrap, time)`;
  a `BlobArray<FixedCurveKey>` of your own works through `AsSpan()`.
- `FromAnimationCurve`/`TryFromAnimationCurve` convert key for key (weighted keys refused), `Bake(curve, samples)`
  samples any curve linearly, `ToAnimationCurve()` for display. Authoring time only: never evaluate an
  `AnimationCurve` in the simulation.

## `FixedHash`

FNV-1a over raw values: `var hash = FixedHash.Empty; hash.Add(position); hash.Add(hp); ... hash.value`. `Add` takes
integers, `bool`, `FixedPoint`, the vectors, `FixedQuaternion`, `FixedPose`, the matrices and `FixedRandom`. A mutable
struct: add to a local. A lockstep simulation does not need it (its checksum covers every component); use it for
state outside one (saves, tests that compare two runs, a game without lockstep).

## Inspector

`Pragma.Dots.Mathematics.Editor` edits `FixedPoint` fields as decimal numbers, `FixedVector2`/`FixedVector3`/
`FixedVector4` as vectors, `FixedQuaternion` as Euler angles in degrees and `FixedCurve` in the curve editor, and
stores raw values: serialized authoring fields (actor parts) can be fixed-point directly, with no float conversion at
bake time.

Limit a `FixedPoint` field with `[FixedMin(min)]` or `[FixedRange(min, max)]` (a slider), never with Unity's `[Min]` or
`[Range]`: their drawers run before the type's drawer and break the field (a raw struct, an error label). The limits
are doubles rounded to the nearest step; on an array they apply to every element; `Clamp(value)` applies them in code.

Angles stay radians in the field: `[FixedDegrees]` shows a `FixedPoint` in degrees (`[FixedDegrees(min, max)]`: a
slider); `[FixedDirection]` edits a `FixedVector2` as one angle from +X and stores the unit vector,
`[FixedDirection(heading: true)]` as the heading on XZ from +Z toward +X (what `FixedQuaternion.RotateY` takes).

## Serialization

Every type is a `[DataContract]` of its public fields (and `[Serializable]`, except the readonly `FixedGrid`):
`JsonUtility` and Newtonsoft.Json write raw values (`{"rawValue":-98304}`) and read them back exactly. A new field of
a math type needs `[DataMember]`.

## Recipes and pitfalls

- **Move toward a target:** `position = FixedMath.MoveTowards(position, target, speed * time.deltaTime);` (`time` is
  the `LockstepTime` singleton)
- **Face a direction on the ground plane:**
  `rotation = FixedQuaternion.LookRotation(new FixedVector3(direction.x, 0, direction.y), FixedVector3.Up);`
- **Normalize safely:** `FixedMath.NormalizeSafe(v)` returns zero (or the given default) for a zero vector;
  `Normalize` of zero also returns zero.
- **Overflow:** `FixedPoint * FixedPoint` of two values around 50 000 overflows silently. Keep world units moderate
  (meters, not millimeters) and use `Length`/`Distance` rather than `LengthSquared` for large coordinates; scale
  large amounts with `MulDiv`, saturate with `TryMultiply`, and find the culprit with `PRAGMA_DOTS_CHECKED_MATH`.
- **Hit tests:** a projectile's path in a tick is `new FixedSegment2(p, p + v * dt)` against the target's
  `FixedCircle` (`TryIntersect(circle, out fraction)`); a line of sight is a `FixedRay2` against walls.
- **Precision:** 1/65536 per step. Accumulating tiny increments (`0.00001` per tick) rounds to zero; scale the unit
  or keep a raw integer accumulator.
- **Division:** truncates toward zero, so `FixedPoint.One / 3 * 3` is one step below 1. Prefer multiplying by a
  precomputed fraction or comparing with a tolerance of a few steps when exact equality is not guaranteed.
- **Ordering:** `FixedPoint` is `IComparable` by its raw value, so sorting by it is deterministic.
- **No float leaks:** `(FixedPoint)Mathf.Sin(x)` inside a system is exactly the bug the type exists to prevent.
  Search simulation code for `float`, `double`, `Mathf`, `math.` before finishing.
