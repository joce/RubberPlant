lsystem MultiIgnore
{
    angle: 90 ;

    action: F, A, B, C -> draw ;

    ignore: +[ ;

    axiom : F-[A-B]-C ;
    rule: +A < F > FA -> F-F+FA ;
    rule:  B < A      -> F-B+F ;
    rule:      B > FF -> C-F+F ;
    rule:      C      -> F-F+F ;
}
