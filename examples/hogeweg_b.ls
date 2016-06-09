// From The Algorithmic Beauty of Plants, p. 35

lsystem hogeweg_b {

    a: F -> draw;
    ignore: +-F;

    axiom : FIFIFI ;
    r: O < O > O -> I;
    r: O < O > I -> I[-FIFI];
    r: O < I > O -> I;
    r: O < I > I -> I;
    r: I < O > O -> O;
    r: I < O > I -> IFI;
    r: I < I > O -> I;
    r: I < I > I -> O;
    r: + -> -;
    r: - -> +;



    angle : 22.5;
}
