#include "MyLeaf.ls"
#include "/Flowers/MyFlower.ls"

lsystem MyPlant
{
    d: angle = 92.2;
    a: F -> draw;
    a: A, B -> nop;

    x: F+FAF++FfAAB++ff+f;
    r: F -> fFf; // Comment
    r: EF} < A > F -> AFABAAF;
    r: f  0.33 -> BBAF ,
          0.33 -> FF ,
          0.33 -> F[FB]A ;
}

d[efine]
a[ction]
x|[axiom]
r[ule]

lsystem MyPlant
{
    let BLAH = 42;
    let myVal = 42;

    angle = 92.2;

    vocabulary
    ====
        F : draw;
        A, B : nop;
        f : move;
    ====

    rules
        axiom -> F+FAF++FfAAB++ff+f;
        F -> fFf; // Comment
        B -> FBBF;
        f  // Stochastic rule
            0.33 -> BBAF ,
            0.33 -> FF ,
            0.33 -> F[FB]A ;


        // context sensitive rules
        EF < A > F -> AFABAAF;
        F < A > B -> FFFAF;

        // parametric rule
        G(a) -> H(a*myVal, 2) -FFF G(a/2);

        // parametric conditional
        H(x, y)
        {
            x > y -> G(x/y);
            x <= y -> G(x/y);
        }

        // context sensitive parametric conditional
        H(a, b) < W(x, y) > G(s)
        {
            x > y -> W(x/y, 1) G(12);
            x <= y -> H(x/y, sqrt(x));
        }
}
