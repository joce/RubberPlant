// From The Algorithmic Beauty of Plants, p. 15

lsystem hilbert {
    vocabulary {
        F: draw ;
    }
    rules {
        axiom -> L ;
        L -> +RF - LFL - FR+ ;
        R -> -LF + RFR + FL- ;
    }
    angle = 90;
}
