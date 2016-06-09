lsystem PrePostConditions
{
    angle: 90;

    action: F, A, B, C -> draw;

    axiom : F-A-B-C ;
    rule: +A   < F > FABB+ -> F-F+FA ;
    rule: FBA+ < A         -> F-B+F ;
    rule:        B > FF    -> C-F+F ;
    rule:        C         -> F-F+F ;
}
