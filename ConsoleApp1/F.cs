public class F
{
    int i1;
    int i2;
    int i3;
    int i4;
    int i5;

    public int[] mas;

    public F()
    {
        i1 = 1; i2 = 2; i3 = 3; i4 = 4; i5 = 5;
        mas = new[] { 1, 2 };
    }

    public F Get() => new F();
}
