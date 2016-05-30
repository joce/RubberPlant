// From The Algorithmic Beauty of Plants, p. 15

lsystem hilbert {

    a: F -> draw ;

    axiom : L ;
    r: L -> +RF - LFL - FR+ ;
    r: R -> -LF + RFR + FL- ;

    angle : 90;
}
