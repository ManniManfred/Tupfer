// Tic-Tac-Toe für den tiptoi-Stift – als C#-Programm für den TipToy Designer.
// Gleiches Verhalten wie tic-tac-toi.yaml: zwei Spieler tippen abwechselnd auf die neun Felder.

class TicTacToe : TipToi
{
    // Wer ist dran? 1 = Kreuz, 2 = Kreis
    int zug = 1;

    // Anzahl der belegten Felder
    int belegt;

    // Spielfeld: 0 = frei, 1 = Kreuz, 2 = Kreis (O/M/U = oben/mitte/unten, L/M/R = links/mitte/rechts)
    int OL, OM, OR, ML, MM, MR, UL, UM, UR;

    [Oid] void feldOL() { if (OL == 0) { OL = zug; Gesetzt(); } else Belegt(OL); }
    [Oid] void feldOM() { if (OM == 0) { OM = zug; Gesetzt(); } else Belegt(OM); }
    [Oid] void feldOR() { if (OR == 0) { OR = zug; Gesetzt(); } else Belegt(OR); }
    [Oid] void feldML() { if (ML == 0) { ML = zug; Gesetzt(); } else Belegt(ML); }
    [Oid] void feldMM() { if (MM == 0) { MM = zug; Gesetzt(); } else Belegt(MM); }
    [Oid] void feldMR() { if (MR == 0) { MR = zug; Gesetzt(); } else Belegt(MR); }
    [Oid] void feldUL() { if (UL == 0) { UL = zug; Gesetzt(); } else Belegt(UL); }
    [Oid] void feldUM() { if (UM == 0) { UM = zug; Gesetzt(); } else Belegt(UM); }
    [Oid] void feldUR() { if (UR == 0) { UR = zug; Gesetzt(); } else Belegt(UR); }

    void Belegt(int feld)
    {
        if (feld == 1)
            Play("here_cross");
        else
            Play("here_circle");
        Ansage();
    }

    void Gesetzt()
    {
        belegt++;
        if (zug == 1)
            Play("here_now_cross");
        else
            Play("here_now_circle");

        if (OL == zug && ML == zug && UL == zug ||
            OM == zug && MM == zug && UM == zug ||
            OR == zug && MR == zug && UR == zug ||
            OL == zug && OM == zug && OR == zug ||
            ML == zug && MM == zug && MR == zug ||
            UL == zug && UM == zug && UR == zug ||
            OL == zug && MM == zug && UR == zug ||
            OR == zug && MM == zug && UL == zug)
        {
            if (zug == 1)
                Play("player1wins");
            else
                Play("player2wins");
            zug = 3 - zug;   // der Verlierer beginnt
            Neu();
        }
        else if (belegt == 9)
        {
            Play("draw");
            Neu();
        }
        else
        {
            zug = 3 - zug;
            Ansage();
        }
    }

    void Neu()
    {
        belegt = 0;
        OL = 0; OM = 0; OR = 0;
        ML = 0; MM = 0; MR = 0;
        UL = 0; UM = 0; UR = 0;
        Ansage();
    }

    void Ansage()
    {
        if (zug == 1)
            Play("player1");
        else
            Play("player2");
    }
}
