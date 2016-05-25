// Inspired from The Algorithmic Beauty of Plants, pp. 28-29

lsystem stochastic {
    vocabulary {
        F: draw;
        f: move;
        a, b, e: nop;
    }
    rules {
        axiom -> [F]----e++++[F]----e++++[F]----e++++[F]----e++++[F]----e++++[F];
        F {
            0.33 -> F[+F]F[-F]F;
            0.33 -> F[+F]F ;
            0.33 -> F[-F]F ;
        }

        e -> f;
        f -> ff;
    }
    angle = 22.5;
}
