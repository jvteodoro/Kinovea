using Kinovea.ScreenManager;

var checks = new Checks();
var a = new Vector3(1, 2, 3);
var b = new Vector3(4, 5, 6);

// Vector value contracts and component-wise arithmetic.
checks.Vector(new Vector3(2), new Vector3(2, 2, 2), "uniform constructor");
checks.Vector(new Vector3(5, 7, 9), a + b, "vector addition");
checks.Vector(new Vector3(-3, -3, -3), a - b, "vector subtraction");
checks.Vector(new Vector3(4, 10, 18), a * b, "component multiplication");
checks.Vector(new Vector3(4, 10, 18), Vector3.Multiply(a, b), "multiply helper");
checks.Vector(new Vector3(2, 4, 6), a * 2, "scalar multiplication");
checks.Vector(new Vector3(0.5f, 1, 1.5f), a / 2, "scalar division");
checks.Vector(new Vector3(0.25f, 0.4f, 0.5f), new Vector3(1, 2, 3) / new Vector3(4, 5, 6), "component division");
checks.Vector(new Vector3(4, 5, 6), Vector3.Add(a, 3), "scalar addition helper");
checks.Vector(new Vector3(-2, -1, 0), Vector3.Subtract(a, 3), "scalar subtraction helper");
checks.Vector(new Vector3(2, 4, 6), Vector3.Multiply(a, 2), "scalar multiply helper");
checks.Vector(new Vector3(0.5f, 1, 1.5f), Vector3.Divide(a, 2), "scalar divide helper");
checks.Vector(new Vector3(-1, -2, -3), a.Negate(), "negate");
checks.Vector(new Vector3(1, 2, 3), new Vector3(-1, -2, -3).Abs(), "absolute value");
checks.Vector(new Vector3(1, 0, 0), new Vector3(1, 0, 0).Inverse(), "inverse preserves zero coordinates");
checks.Equal(14f, a.Square, "square norm");
checks.Near((float)Math.Sqrt(14), a.Norm, 1e-6f, "norm");
checks.Equal(32f, Vector3.Dot(a, b), "dot product");
checks.Vector(new Vector3(-3, 6, -3), Vector3.Cross(a, b), "cross product");
checks.Equal(0f, Vector3.Dot(a, Vector3.Cross(a, b)), "cross product orthogonal to first vector");
checks.Vector(new Vector3(1, 2, 3), new Vector3(1, 2, 3).ToArray(), "vector array");
checks.Equal("1, 2, 3", a.ToString(), "invariant vector string");
checks.True(a == new Vector3(1, 2, 3), "vector equality operator");
checks.True(a.Equals((object)new Vector3(1, 2, 3)), "boxed vector equality");
checks.True(a != b, "vector inequality operator");
checks.True(a.GetHashCode() == new Vector3(1, 2, 3).GetHashCode(), "equal vector hash consistency");
checks.Equal(3f, new Vector3(3, 2, 1).Max, "maximum coordinate");
checks.Equal(1f, new Vector3(3, 2, 1).Min, "minimum coordinate");
checks.Equal(0, new Vector3(5, 5, 4).MaxIndex, "maximum tie favors lowest index");
checks.Equal(0, new Vector3(1, 1, 2).MinIndex, "minimum tie favors lowest index");
checks.Equal(2, new Vector3(1, 2, 3).MaxIndex, "later unique maximum index");
checks.Equal(2, new Vector3(3, 2, 1).MinIndex, "later unique minimum index");
var normal = new Vector3(3, 0, 4);
checks.Equal(5f, normal.Normalize(), "normalize returns original norm");
checks.Vector(new Vector3(0.6f, 0, 0.8f), normal, "normalize mutates to unit vector");
var zeroNormal = new Vector3();
checks.Equal(0f, zeroNormal.Normalize(), "zero normalize returns zero");
checks.True(float.IsNaN(zeroNormal.X) && float.IsNaN(zeroNormal.Y) && float.IsNaN(zeroNormal.Z), "zero normalize yields NaN coordinates");

// Matrix construction, indexing, products, transforms and exact equality.
var identity = Matrix3x3.Identity;
var matrix = Matrix3x3.CreateFromRows(new Vector3(1, 2, 3), new Vector3(0, 1, 4), new Vector3(5, 6, 0));
checks.Matrix(new float[] { 1, 2, 3, 0, 1, 4, 5, 6, 0 }, matrix.ToArray(), "row-major matrix array");
checks.Matrix(matrix, Matrix3x3.CreateFromColumns(new Vector3(1, 0, 5), new Vector3(2, 1, 6), new Vector3(3, 4, 0)), "column constructor");
checks.Matrix(new float[] { 7, 0, 0, 0, 8, 0, 0, 0, 9 }, Matrix3x3.CreateDiagonal(new Vector3(7, 8, 9)).ToArray(), "diagonal constructor");
checks.Vector(new Vector3(1, 2, 3), matrix.GetRow(0), "get row");
checks.Vector(new Vector3(2, 1, 6), matrix.GetColumn(1), "get column");
checks.Throws<ArgumentException>(() => matrix.GetRow(-1), "negative row index");
checks.Throws<ArgumentException>(() => matrix.GetRow(3), "high row index");
checks.Throws<ArgumentException>(() => matrix.GetColumn(-1), "negative column index");
checks.Throws<ArgumentException>(() => matrix.GetColumn(3), "high column index");
checks.Matrix(matrix, identity * matrix, "left identity multiplication");
checks.Matrix(matrix, matrix * identity, "right identity multiplication");
checks.Matrix(new float[] { 2, 6, 12, 0, 3, 16, 10, 18, 0 }, matrix * Matrix3x3.CreateDiagonal(new Vector3(2, 3, 4)), "matrix multiplication order");
checks.Matrix(new float[] { 2, 4, 6, 0, 2, 8, 10, 12, 0 }, matrix * 2, "matrix scale");
checks.Matrix(new float[] { 2, 3, 4, 1, 2, 5, 6, 7, 1 }, matrix + 1, "matrix scalar addition");
checks.Matrix(new float[] { 2, 3, 4, 1, 2, 5, 6, 7, 1 }, Matrix3x3.Add(matrix, 1), "matrix scalar add helper");
checks.Matrix(new float[] { 2, 4, 6, 0, 2, 8, 10, 12, 0 }, Matrix3x3.Multiply(matrix, 2), "matrix scale helper");
checks.Matrix(matrix + identity, Matrix3x3.Add(matrix, identity), "matrix add helper");
checks.Matrix(matrix - identity, Matrix3x3.Subtract(matrix, identity), "matrix subtract helper");
checks.Vector(new Vector3(14, 14, 17), matrix * new Vector3(1, 2, 3), "matrix vector product");
checks.Vector(matrix * new Vector3(1, 2, 3), Matrix3x3.Multiply(matrix, new Vector3(1, 2, 3)), "matrix vector helper");
checks.Matrix(new float[] { 1, 0, 5, 2, 1, 6, 3, 4, 0 }, matrix.Transpose().ToArray(), "transpose");
checks.Matrix(new float[] { 14, 14, 17, 14, 17, 6, 17, 6, 61 }, matrix.MultiplySelfByTranspose().ToArray(), "matrix times transpose");
checks.Matrix(new float[] { 26, 32, 3, 32, 41, 10, 3, 10, 25 }, matrix.MultiplyTransposeBySelf().ToArray(), "transpose times matrix");
checks.True(matrix == Matrix3x3.CreateFromRows(new Vector3(1, 2, 3), new Vector3(0, 1, 4), new Vector3(5, 6, 0)), "matrix exact equality");
checks.True(matrix != identity, "matrix inequality");
checks.True(matrix.Equals((object)matrix), "boxed matrix equality");
checks.True(matrix.GetHashCode() == matrix.GetHashCode(), "matrix hash repeatability");

// Determinant, adjugate and inverse, including exact singular and near-singular inputs.
checks.Equal(1f, matrix.Determinant, "determinant");
checks.Matrix(new float[] { -24, 18, 5, 20, -15, -4, -5, 4, 1 }, matrix.Adjugate().ToArray(), "adjugate");
var inverse = matrix.Inverse();
checks.Matrix(new float[] { -24, 18, 5, 20, -15, -4, -5, 4, 1 }, inverse.ToArray(), "inverse for unit determinant");
checks.Matrix(identity, matrix * inverse, "matrix times inverse");
checks.Equal(0f, Matrix3x3.CreateDiagonal(new Vector3(1, 2, 0)).Determinant, "singular determinant");
checks.Throws<ArgumentException>(() => Matrix3x3.CreateDiagonal(new Vector3(1, 2, 0)).Inverse(), "singular inverse throws");
var nearSingular = Matrix3x3.CreateDiagonal(new Vector3(1, 1, 1e-6f));
checks.Near(1e-6f, nearSingular.Determinant, 1e-10f, "near-singular determinant");
checks.Near(1e6f, nearSingular.Inverse().V22, 1f, "near-singular inverse has no threshold rejection");
var pseudoInverse = matrix.PseudoInverse();
checks.Matrix(inverse, pseudoInverse, "full-rank pseudo-inverse agrees with inverse");
var rankDeficientPseudoInverse = Matrix3x3.CreateDiagonal(new Vector3(1, 2, 0)).PseudoInverse();
checks.Matrix(new float[] { 1, 0, 0, 0, 0.5f, 0, 0, 0, 0 }, rankDeficientPseudoInverse, "rank-deficient pseudo-inverse");
matrix.SVD(out var u, out var singularValues, out var v);
checks.Matrix(matrix, u * Matrix3x3.CreateDiagonal(singularValues) * v.Transpose(), "SVD reconstructs source matrix");

// Rotation conventions and rotation extraction.
var quarterTurn = (float)(Math.PI / 2);
checks.Vector(new Vector3(0, -1, 0), Matrix3x3.CreateRotationX(quarterTurn) * new Vector3(0, 0, 1), "x rotation convention");
checks.Vector(new Vector3(1, 0, 0), Matrix3x3.CreateRotationY(quarterTurn) * new Vector3(0, 0, 1), "y rotation convention");
checks.Vector(new Vector3(0, 1, 0), Matrix3x3.CreateRotationZ(quarterTurn) * new Vector3(1, 0, 0), "z rotation convention");
var yaw = 0.2f;
var pitch = -0.3f;
var roll = 0.4f;
var rotation = Matrix3x3.CreateFromYawPitchRoll(yaw, pitch, roll);
rotation.ExtractYawPitchRoll(out var actualYaw, out var actualPitch, out var actualRoll);
checks.Near(yaw, actualYaw, 1e-6f, "extract yaw");
checks.Near(pitch, actualPitch, 1e-6f, "extract pitch");
checks.Near(roll, actualRoll, 1e-6f, "extract roll");

Console.WriteLine($"PASS: {checks.Count} geometry characterization assertions");

sealed class Checks
{
    public int Count { get; private set; }

    public void True(bool actual, string name) => Equal(true, actual, name);

    public void Equal<T>(T expected, T actual, string name)
    {
        Count++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{name}: expected <{expected}>, got <{actual}>");
    }

    public void Near(float expected, float actual, float tolerance, string name)
    {
        Count++;
        if (float.IsNaN(actual) || MathF.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException($"{name}: expected {expected} +/- {tolerance}, got {actual}");
    }

    public void Vector(Vector3 expected, Vector3 actual, string name)
    {
        Near(expected.X, actual.X, 1e-6f, name + " X");
        Near(expected.Y, actual.Y, 1e-6f, name + " Y");
        Near(expected.Z, actual.Z, 1e-6f, name + " Z");
    }

    public void Vector(Vector3 expected, float[] actual, string name) =>
        Vector(expected, new Vector3(actual[0], actual[1], actual[2]), name);

    public void Matrix(Matrix3x3 expected, Matrix3x3 actual, string name) =>
        Matrix(expected.ToArray(), actual.ToArray(), name);

    public void Matrix(float[] expected, Matrix3x3 actual, string name) =>
        Matrix(expected, actual.ToArray(), name);

    public void Matrix(float[] expected, float[] actual, string name)
    {
        Equal(expected.Length, actual.Length, name + " length");
        for (var i = 0; i < expected.Length; i++)
            Near(expected[i], actual[i], 1e-6f, $"{name}[{i}]");
    }

    public void Throws<TException>(Action action, string name) where TException : Exception
    {
        Count++;
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException($"{name}: expected {typeof(TException).Name}");
    }
}
