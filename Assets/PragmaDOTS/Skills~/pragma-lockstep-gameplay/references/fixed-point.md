# Fixed-point math reference

Namespace and assembly `Pragma.Dots.Mathematics`: part of the core, always compiled, no Entities dependency (it
references only Unity.Mathematics and Burst). The recipes below use lockstep types (`LockstepTime`,
`LockstepSessionInfo`).

| Type | Role | Float counterpart |
|---|---|---|
| `FixedPoint` | Scalar | `float` |
| `FixedVector2`, `FixedVector3` | Vectors | `float2` / `Vector2`, `float3` / `Vector3` |
| `FixedQuaternion` | Rotation | `quaternion` / `Quaternion` |
| `FixedPose` | Position and rotation | `Pose` |
| `FixedMath` | Functions and constants | `Mathf`, `math` |
| `FixedRandom` | Random numbers | `Unity.Mathematics.Random` |
| `FixedGrid` | Cells of a grid on the XZ plane | |

## Contents

- `FixedPoint` format and limits
- Creating values
- Operators
- `FixedMath` functions
- Vectors
- Quaternions and poses
- `FixedRandom`
- `FixedGrid`
- Inspector
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
| `+`, `-`, unary `-` | Exact |
| `FixedPoint * FixedPoint` | Rounded to the nearest step, halves up |
| `FixedPoint * int`, `int * FixedPoint` | Exact |
| `FixedPoint / FixedPoint`, `FixedPoint / int` | Truncated toward zero; division by zero saturates to `MaxValue`/`MinValue` (or 0 for 0/0) instead of throwing |
| `%` | Sign of the dividend, like C#; zero divisor returns 0 |
| `==`, `!=`, `<`, `>`, `<=`, `>=`, `++`, `--` | As expected |
| `(int)x`, `(long)x` | Truncate toward zero |
| `(float)x`, `(double)x`, `ToString()` | Display only |

## `FixedMath` functions

| Group | Functions |
|---|---|
| Basic | `Abs`, `Sign` (-1/0/1), `Min`, `Max`, `Clamp`, `Clamp01`, `Lerp`, `InverseLerp` (0 when a == b), `Remap`, `MulDiv(a, b, c)`, `Select(falseValue, trueValue, test)`, `Step`, `SmoothStep`, `MoveTowards` |
| Rounding | `Floor`, `Ceil`, `Round` (halves up), `Truncate`, `Frac`, `FloorToInt`, `CeilToInt`, `RoundToInt` |
| Modulo | `Remainder` (sign of the dividend, like `%`), `Mod` (in `[0, Abs(y))`), `Wrap(x, min, max)` |
| Powers | `Sqrt` (±1 step below about 2e9), `Rsqrt`, `Exp`, `Exp2`, `Log`, `Log2`, `Log10`, `Pow(FixedPoint, FixedPoint)`, `Pow(FixedPoint, int)` |
| Trigonometry | `Sin`, `Cos`, `SinCos`, `Tan`, `Asin`, `Acos`, `Atan`, `Atan2(y, x)` - radians |
| Angles | `ToRadians(degrees)`, `ToDegrees(radians)`, `DeltaAngle(from, to)` in `[-Pi, Pi)` |
| Constants | `Pi`, `TwoPi`, `HalfPi`, `E`, `Ln2`, `Ln10`, `Sqrt2`, `Rad2Deg`, `Deg2Rad`, `Epsilon` |
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
- Conversions: implicit from `int2`/`int3` (not back: `FixedMath.FloorToInt` per component or
  `FixedGrid.WorldToCell`); `float2`, `float3`, `Vector2`, `Vector3` explicit both ways (display and bake time only).

## Quaternions and poses

`FixedQuaternion { x, y, z, w }`, laid out like `Unity.Mathematics.quaternion`:

- `Identity`, `AxisAngle(axis, angle)`, `RotateX/Y/Z(angle)`, `Euler(FixedVector3 radians)` (Unity's order: z, then
  x, then y), `LookRotation(forward, up)`, `FromBasis(right, up, forward)`, `Xyz`.
- `q * v` rotates a vector; `a * b` applies `b` first (Hamilton product).
- `FixedMath`: `Mul`, `Rotate`, `Conjugate`, `Inverse`, `Dot`, `Length`, `Normalize`, `NormalizeSafe`, `Nlerp`,
  `Slerp`, `Angle(a, b)`, `RotateTowards(from, to, maxRadians)`, `Forward(q)`, `Up(q)`, `Right(q)`.
- Explicit conversions to and from `quaternion` and `Quaternion`. Normalize after many multiplications.
- `FixedPose { position, rotation }`: `Identity`, `Forward`, `Up`, `Right`, equality. The placement of actors
  (`IActorBuilder.Placement`); `LockstepTransform.FromPose(pose, scale)` turns it into a lockstep transform.

## `FixedRandom`

PCG32 (XSH-RR), 128 bits of state, seeded through SplitMix64. A struct: store it in a component and access it by
`ref`, or the state does not advance.

| Member | Result |
|---|---|
| `new FixedRandom(seed)`, `new FixedRandom(seed, sequence)` | Generator; different sequences are independent streams |
| `FixedRandom.CreateFromIndex(seed, index)` | Per-index stream, e.g. `(LockstepSessionInfo.seed, lockstepEntityId.value)` once the id is assigned |
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

## Inspector

`Pragma.Dots.Mathematics.Editor` edits `FixedPoint` fields as decimal numbers, `FixedVector2`/`FixedVector3` as
vectors and `FixedQuaternion` as Euler angles in degrees, and stores raw values: serialized authoring fields (actor
parts) can be fixed-point directly, with no float conversion at bake time.

## Recipes and pitfalls

- **Move toward a target:** `position = FixedMath.MoveTowards(position, target, speed * time.deltaTime);` (`time` is
  the `LockstepTime` singleton)
- **Face a direction on the ground plane:**
  `rotation = FixedQuaternion.LookRotation(new FixedVector3(direction.x, 0, direction.y), FixedVector3.Up);`
- **Normalize safely:** `FixedMath.NormalizeSafe(v)` returns zero (or the given default) for a zero vector;
  `Normalize` of zero also returns zero.
- **Overflow:** `FixedPoint * FixedPoint` of two values around 50 000 overflows silently. Keep world units moderate
  (meters, not millimeters) and use `Length`/`Distance` rather than `LengthSquared` for large coordinates.
- **Precision:** 1/65536 per step. Accumulating tiny increments (`0.00001` per tick) rounds to zero; scale the unit
  or keep a raw integer accumulator.
- **Division:** truncates toward zero, so `FixedPoint.One / 3 * 3` is one step below 1. Prefer multiplying by a
  precomputed fraction or comparing with a tolerance of a few steps when exact equality is not guaranteed.
- **Ordering:** `FixedPoint` is `IComparable` by its raw value, so sorting by it is deterministic.
- **No float leaks:** `(FixedPoint)Mathf.Sin(x)` inside a system is exactly the bug the type exists to prevent.
  Search simulation code for `float`, `double`, `Mathf`, `math.` before finishing.
