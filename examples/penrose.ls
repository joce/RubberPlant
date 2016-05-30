// Adapted from javalin samples (https://github.com/jdf/javalin/tree/master/samples)

lsystem penrose {

    a: A -> draw ;

    axiom : [C]++[C]++[C]++[C]++[C] ;
    r: A -> ;
    r: B -> DA++EA----CA[-DA----BA]++ ;
    r: C -> +DA--EA[---BA--CA]+ ;
    r: D -> -BA++CA[+++DA++EA]- ;
    r: E -> --DA++++BA[+EA++++CA]--CA;

    angle : 36;
}
