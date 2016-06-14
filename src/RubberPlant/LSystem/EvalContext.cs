using System.Collections.Generic;

namespace RubberPlant
{
    public class EvalContext
    {
        // TODO The following should be tested in the LSystem tests.

        // Left context is expected to be filled by the head, so that a sting such as
        // ABCDEFG will successively represented by the following contexts as is it
        // interpreted, where the first block (if present) represents the left context,
        // the middle single letter, the current atom, and the last block (if present)
        // represents the right context.
        //
        //  A BCDEFG
        // A B CDEFG
        // BA C DEFG
        // CBA D EFG
        // DCBA E FG
        // EDCBA F G
        // FEDCBA G

        public List<Atom> Left { get; set; }
        public Atom Current { get; set; }
        public List<Atom> Right { get; set; }
    }
}
