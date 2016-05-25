// Adapted from javalin samples (https://github.com/jdf/javalin/tree/master/samples)

lsystem penrose {
    vocabulary {
        A: draw ;
    }
    rules {
        axiom -> [C]++[C]++[C]++[C]++[C] ;
        A -> ;
        B -> DA++EA----CA[-DA----BA]++ ;
        C -> +DA--EA[---BA--CA]+ ;
        D -> -BA++CA[+++DA++EA]- ;
        E -> --DA++++BA[+EA++++CA]--CA;
    }
    angle = 36;
}
