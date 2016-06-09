lsystem ImpossiblePrePostConditions
{
    angle: 90;

    action: F, A, B, C -> draw;

    axiom : F-B-C ;
    rule: +A < F > FA -> F-F+F ;
    rule:      B > FF -> C-F+F ;
    rule:      C      -> F-F+F ;
}
