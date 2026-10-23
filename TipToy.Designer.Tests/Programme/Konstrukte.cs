class Test : TipToi
{
    const int Max = 7;
    int a, b = 3, c, zaehler, modus;
    bool aktiv;
    int x, y;

    [Oid] void TasteA()
    {
        a = (a + 1) % 10;
        if (a > 5 && !aktiv) Play("gross");
        else if (a == 3 || a == 7) { Play("drei_sieben"); b = b * 2 % 50; }
        else if (!(a < 2)) Play("mittel");
        else { Play("klein"); aktiv = !aktiv; }
    }

    [Oid] void TasteB()
    {
        int t = a * 3 + b;
        x = t - (a & 1) - (b >> 1);
        y = x << 2 | 1;
        if (x + y > 40) { Play("viel"); Zaehle(2); } else Zaehle(1);
        switch (modus)
        {
            case 0: Play("modus_null"); modus = 1; break;
            case 1: case 2: Play("modus_eins_zwei"); modus++; break;
            default: Play("modus_rest"); modus = 0; break;
        }
        Play("ende_b");
    }

    void Zaehle(int n)
    {
        zaehler += n;
        if (zaehler > Max) { zaehler = 0; Play("ueberlauf"); return; }
        Play("gezaehlt");
    }

    [Oid] void TasteC()
    {
        a = 1; b = 2; c = 3; x = 4; y = 5; zaehler = 6 % (Max + 1); modus = 2; c += a; c += b; c *= 2;
        aktiv = c > 10;
        bool gerade = (c & 1) == 0 && c != 0;
        if (aktiv) Play("aktiv"); else Play("inaktiv");
        if (gerade) Play("gerade");
    }

    [Oid] void TasteD()
    {
        c = c + 1;
        if (c > 3) c = 0;
        Play("d");
        if (c == 0) return;
        Play("d_nicht_null");
        Verschachtelt();
    }

    void Verschachtelt()
    {
        if (a < 5)
        {
            if (b < 10) { b++; Play("b_klein"); }
            else { b = 0; Play("b_reset"); }
            Play("nach_innen");
        }
        Play("nach_aussen");
    }

    [Oid] void TasteE() => Tausche(x, y);

    void Tausche(int p, int q)
    {
        x = q % 100;
        y = p % 100;
        PlayAll("tausch", "fertig");
        if (x == y) PlayRandom("gleich", "auch_gleich");
    }
}
