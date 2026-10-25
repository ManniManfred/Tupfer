class Schleifen : TipToi
{
    const int N = 5;
    int summe, quadrate, a, b, c, runde, zaehler;
    bool gefunden;

    [Oid] void Summe()
    {
        summe = 0;
        for (int i = 1; i <= N; i++)
            summe += i;
        Play("summe");
    }

    [Oid] void Quadrate()
    {
        quadrate = 0;
        for (int i = 0; i < 4; i++)
        {
            int q = i * i;
            quadrate += q;
            if (i == 2)
                Play("zwei");
        }
        for (int k = 10; k > 0; k -= 3)
            Play("k");
        for (int k = 3; 0 <= k; --k)
            zaehler += k;
        zaehler %= 100;
    }

    [Oid] void Verschachtelt()
    {
        runde = (runde + 1) % 10;
        for (int x = 0; x < 3; x++)
            for (int y = 0; y < 3; y++)
                if (x * 3 + y == runde)
                {
                    a = x;
                    b = y;
                    Play("treffer");
                }
    }

    [Oid] void Liste()
    {
        gefunden = false;
        foreach (var z in new[] { 2, 4, 6 })
        {
            c += z;
            if (c % 4 == 0)
                gefunden = true;
        }
        c %= 50;
        if (gefunden)
            Play("gefunden");
    }

    [Oid] void MitMethode()
    {
        for (int i = 0; i < 3; i++)
            Melde(i);
    }

    void Melde(int wert)
    {
        if (wert == 0) { Play("null"); return; }
        a += wert;
        a %= 20;
        Play("mehr");
    }
}
