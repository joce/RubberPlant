// From The Algorithmic Beauty of Plants, p. 35

lsystem hogeweg_d {

    a: F -> draw;
    ignore: +-F;

    axiom : FOFIFI ;
    r: O < O > O -> I;
    r: O < O > I -> O;
    r: O < I > O -> O;
    r: O < I > I -> IFI;
    r: I < O > O -> I;
    r: I < O > I -> I[+FIFI];
    r: I < I > O -> I;
    r: I < I > I -> O;
    r: + -> -;
    r: - -> +;

    angle : 25.75;
}
