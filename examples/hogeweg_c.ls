// From The Algorithmic Beauty of Plants, p. 35

lsystem hogeweg_c {

    a: F -> draw;
    ignore: +-F;

    axiom : FIFIFI ;
    r: O < O > O -> O;
    r: O < O > I -> I;
    r: O < I > O -> O;
    r: O < I > I -> I[+FIFI];
    r: I < O > O -> O;
    r: I < O > I -> IFI;
    r: I < I > O -> O;
    r: I < I > I -> O;
    r: + -> -;
    r: - -> +;

    angle : 25.75;
}
