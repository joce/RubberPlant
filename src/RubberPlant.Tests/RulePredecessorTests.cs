using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace RubberPlant.Tests
{
    [TestFixture]
    public class RulePredecessorTests
    {
        [Test]
        public void RuleToStringPrintsAsExpectedWithoutPreOrPostCondition()
        {
            // Upper case
            for (int i = 0; i < 26; i++)
            {
                char ruleID = (char)('A' + i);
                RulePredecessor r = new RulePredecessor {RuleID = ruleID};
                Assert.AreEqual(ruleID.ToString(), r.ToString());
            }

            // Lower case
            for (int i = 0; i < 26; i++)
            {
                char ruleID = (char)('a' + i);
                RulePredecessor r = new RulePredecessor {RuleID = ruleID};
                Assert.AreEqual(ruleID.ToString(), r.ToString());
            }

            // Legal special characters
            foreach (var turtleCommand in LSystem.k_implicitTurtleCommands.Keys)
            {
                char ruleID = turtleCommand.RuleName;
                RulePredecessor r = new RulePredecessor {RuleID = ruleID};
                Assert.AreEqual(ruleID.ToString(), r.ToString());
            }
        }

        [Test]
        public void RuleToStringPrintsAsExpectedWithPreCondition()
        {
            RulePredecessor r = new RulePredecessor {RuleID = 'A', PreCondition = "BBa]A".ToAtoms()};
            Assert.AreEqual("BBa]A < A", r.ToString());
        }

        [Test]
        public void RuleToStringPrintsAsExpectedWithPostCondition()
        {
            RulePredecessor r = new RulePredecessor {RuleID = 'A', PostCondition = "BBa]A".ToAtoms()};
            Assert.AreEqual("A > BBa]A", r.ToString());
        }

        [Test]
        public void RuleToStringPrintsAsExpectedWithPreOrPostCondition()
        {
            RulePredecessor r = new RulePredecessor
            {
                RuleID = 'A',
                PreCondition = "FFf}B".ToAtoms(),
                PostCondition = "BBa]A".ToAtoms()
            };
            Assert.AreEqual("FFf}B < A > BBa]A", r.ToString());
        }

        [Test]
        public void RuleEqualityWorksAsExpected()
        {
            RulePredecessor rA = new RulePredecessor
            {
                RuleID = 'A',
                // Reversed,  because that's how it's going to be read in by the parser
                PreCondition = "FFf}B".Reverse().ToAtoms(),
                PostCondition = "BBa]A".ToAtoms()
            };

            // Same as rA
            RulePredecessor rA1 = new RulePredecessor
            {
                RuleID = 'A',
                // Reversed,  because that's how it's going to be read in by the parser
                PreCondition = "FFf}B".Reverse().ToAtoms(),
                PostCondition = "BBa]A".ToAtoms()
            };

            // Same as rA, but no precondition
            RulePredecessor rAPre = new RulePredecessor
            {
                RuleID = 'A',
                // Reversed,  because that's how it's going to be read in by the parser
                PreCondition = "FFf}B".Reverse().ToAtoms(),
            };

            // Same as rA, but no post-condition
            RulePredecessor rAPost = new RulePredecessor
            {
                RuleID = 'A',
                PostCondition = "BBa]A".ToAtoms()
            };

            // Same as rA, but different pre condition
            RulePredecessor rADiffPre = new RulePredecessor
            {
                RuleID = 'A',
                // Reversed,  because that's how it's going to be read in by the parser
                PreCondition = "FFf}".Reverse().ToAtoms(),
                PostCondition = "BBa]A".ToAtoms()
            };

            // Same as rA, but different post-condition
            RulePredecessor rADiffPost = new RulePredecessor
            {
                RuleID = 'A',
                // Reversed,  because that's how it's going to be read in by the parser
                PreCondition = "FFf}B".Reverse().ToAtoms(),
                PostCondition = "Ba]A".ToAtoms()
            };

            // Same rule as rA, but no pre or post-condition
            RulePredecessor rAPlain = new RulePredecessor
            {
                RuleID = 'A'
            };

            // Same pre and post-condition as rA, but different rule ID
            RulePredecessor rB = new RulePredecessor
            {
                RuleID = 'B',
                // Reversed,  because that's how it's going to be read in by the parser
                PreCondition = "FFf}B".Reverse().ToAtoms(),
                PostCondition = "BBa]A".ToAtoms()
            };

            Assert.True(rA == rA1);
            Assert.False(rA == rAPre);
            Assert.False(rA == rAPost);
            Assert.False(rA == rADiffPre);
            Assert.False(rA == rADiffPost);
            Assert.False(rA == rAPlain);
            Assert.False(rA == rB);
        }

        // TODO Refactor the match tests. Lots and lots of duplication that could go away.

        [Test]
        public void SimpleMatchWorks()
        {
            RulePredecessor desc = new RulePredecessor {RuleID = 'F'};
            EvalContext ctx = new EvalContext
            {
                Current = 'F',
                // Reversed, because that's how the LSystem is expected to present it
                Left = "FFf}B".Reverse().ToAtoms(),
                Right = "BBa]A".ToAtoms()
            };

            Assert.IsTrue(desc.Match(ctx));
        }

        [Test]
        public void SimpleNonMatchWorks()
        {
            RulePredecessor desc = new RulePredecessor {RuleID = 'F'};
            EvalContext ctx = new EvalContext
            {
                Current = 'B',
                // Reversed, because that's how the LSystem is expected to present it
                Left = "FFf}B".Reverse().ToAtoms(),
                Right = "BBa]A".ToAtoms()
            };

            Assert.IsFalse(desc.Match(ctx));
        }

        [Test]
        public void MatchWithSimplePreconditionWorks()
        {
            RulePredecessor desc = new RulePredecessor
            {
                RuleID = 'F',
                // Reversed,  because that's how it's going to be read in by the parser
                PreCondition = "FFf-".Reverse().ToAtoms()
            };

            EvalContext ctx = new EvalContext
            {
                Current = 'F',
                // Reversed, because that's how the LSystem is expected to present it
                Left = "BFFf-".Reverse().ToAtoms(),
                Right = "BBa]A".ToAtoms()
            };

            Assert.IsTrue(desc.Match(ctx));
        }

        [Test]
        public void NonMatchWithSimplePreconditionWorks()
        {
            RulePredecessor desc = new RulePredecessor
            {
                RuleID = 'F',
                // Reversed,  because that's how it's going to be read in by the parser
                PreCondition = "FB".Reverse().ToAtoms()
            };

            EvalContext ctx = new EvalContext
            {
                Current = 'F',
                // Reversed, because that's how the LSystem is expected to present it
                Left = "FFf}B".Reverse().ToAtoms(),
                Right = "BBa]A".ToAtoms()
            };

            Assert.IsFalse(desc.Match(ctx));
        }

        [Test]
        public void MatchWithSimplePostconditionWorks()
        {
            RulePredecessor desc = new RulePredecessor
            {
                RuleID = 'F',
                PostCondition = "BB+".ToAtoms()
            };

            EvalContext ctx = new EvalContext
            {
                Current = 'F',
                // Reversed, because that's how the LSystem is expected to present it
                Left = "FFf}B".Reverse().ToAtoms(),
                Right = "BB+]A".ToAtoms()
            };

            Assert.IsTrue(desc.Match(ctx));
        }

        [Test]
        public void NonMatchWithSimplePostconditionWorks()
        {
            RulePredecessor desc = new RulePredecessor
            {
                RuleID = 'F',
                PostCondition = "FBf".ToAtoms()
            };

            EvalContext ctx = new EvalContext
            {
                Current = 'F',
                // Reversed, because that's how the LSystem is expected to present it
                Left = "FFf}B".Reverse().ToAtoms(),
                Right = "BBa]A".ToAtoms()
            };

            Assert.IsFalse(desc.Match(ctx));
        }

        [Test]
        public void MatchWithSimpleConditionsWorks()
        {
            RulePredecessor desc = new RulePredecessor
            {
                RuleID = 'F',
                // Reversed,  because that's how it's going to be read in by the parser
                PreCondition = "FFf+".Reverse().ToAtoms(),
                PostCondition = "BBa".ToAtoms()
            };

            EvalContext ctx = new EvalContext
            {
                Current = 'F',
                // Reversed, because that's how the LSystem is expected to present it
                Left = "BFFf+".Reverse().ToAtoms(),
                Right = "BBa]A".ToAtoms()
            };

            Assert.IsTrue(desc.Match(ctx));
        }

        [Test]
        public void NonMatchWithSimpleConditionsWorks()
        {
            RulePredecessor desc = new RulePredecessor
            {
                RuleID = 'F',
                // Reversed,  because that's how it's going to be read in by the parser
                PreCondition = "FFf}".Reverse().ToAtoms(),
                PostCondition = "FBf".ToAtoms()
            };

            EvalContext ctx = new EvalContext
            {
                Current = 'F',
                // Reversed, because that's how the LSystem is expected to present it
                Left = "FFf}B".Reverse().ToAtoms(),
                Right = "BBa]A".ToAtoms()
            };

            Assert.IsFalse(desc.Match(ctx));
        }

        [Test]
        public void MatchWithLeftContextShorterThanPreconditionFails()
        {
            RulePredecessor desc = new RulePredecessor
            {
                RuleID = 'F',
                // Reversed,  because that's how it's going to be read in by the parser
                PreCondition = "FFf}".Reverse().ToAtoms()
            };

            EvalContext ctx = new EvalContext
            {
                Current = 'F',
                // Reversed, because that's how the LSystem is expected to present it
                Left = "FFf".Reverse().ToAtoms()
            };

            Assert.IsFalse(desc.Match(ctx));
        }

        [Test]
        public void MatchWithRightContextShorterThanPostconditionFails()
        {
            RulePredecessor desc = new RulePredecessor
            {
                RuleID = 'F',
                PostCondition = "BBf".ToAtoms()
            };

            EvalContext ctx = new EvalContext
            {
                Current = 'F',
                Right = "BB".ToAtoms()
            };

            Assert.IsFalse(desc.Match(ctx));
        }

        [Test]
        public void MatchWithIgnoresInPreConditionWorks()
        {
            RulePredecessor desc = new RulePredecessor
            {
                RuleID = 'F',
                // Reversed,  because that's how it's going to be read in by the parser
                PreCondition = "FFf".Reverse().ToAtoms()
            };

            EvalContext ctx = new EvalContext
            {
                Current = 'F',
                // Reversed, because that's how the LSystem is expected to present it
                Left = "-F-+Ff".Reverse().ToAtoms()
            };

            IList<Atom> ignores = "-+".ToAtoms();
            Assert.IsTrue(desc.Match(ctx, ignores));
        }

        [Test]
        public void MatchWithIgnoresInPostConditionWorks()
        {
            RulePredecessor desc = new RulePredecessor
            {
                RuleID = 'F',
                PostCondition = "FFf".ToAtoms()
            };

            EvalContext ctx = new EvalContext
            {
                Current = 'F',
                Right = "-F-+Ff".ToAtoms()
            };

            IList<Atom> ignores = "-+".ToAtoms();
            Assert.IsTrue(desc.Match(ctx, ignores));
        }

        [Test]
        public void MatchWithPreconditionWithSimplePushedBranchWorks()
        {
            RulePredecessor desc = new RulePredecessor
            {
                RuleID = 'F',
                // Reversed,  because that's how it's going to be read in by the parser
                PreCondition = "FFf".Reverse().ToAtoms()
            };

            EvalContext ctx = new EvalContext
            {
                Current = 'F',
                // Reversed, because that's how the LSystem is expected to present it
                Left = "FF[AB]f".Reverse().ToAtoms()
            };

            Assert.IsTrue(desc.Match(ctx));
        }

        [Test]
        public void MatchWithPreconditionWithSimplePushedBranchAndBranchMatchWorks()
        {
            RulePredecessor desc = new RulePredecessor
            {
                RuleID = 'F',
                // Reversed,  because that's how it's going to be read in by the parser
                PreCondition = "FF[A]f".Reverse().ToAtoms()
            };

            EvalContext ctx = new EvalContext
            {
                Current = 'F',
                // Reversed, because that's how the LSystem is expected to present it
                Left = "FF[BA]f".Reverse().ToAtoms()
            };

            Assert.IsTrue(desc.Match(ctx));
        }

        [Test]
        public void MatchWithPostconditionWithSimplePushedBranchWorks()
        {
            RulePredecessor desc = new RulePredecessor
            {
                RuleID = 'F',
                PostCondition = "FFf".ToAtoms()
            };

            EvalContext ctx = new EvalContext
            {
                Current = 'F',
                Right = "FF[AB]f".ToAtoms()
            };

            Assert.IsTrue(desc.Match(ctx));
        }

        [Test]
        public void MatchWithPostconditionWithSimplePushedBranchAndBranchMatchWorks()
        {
            RulePredecessor desc = new RulePredecessor
            {
                RuleID = 'F',
                PostCondition = "FF[A]f".ToAtoms()
            };

            EvalContext ctx = new EvalContext
            {
                Current = 'F',
                Right = "FF[AB]f".ToAtoms()
            };

            Assert.IsTrue(desc.Match(ctx));
        }

        [Test]
        public void MatchWithPreconditionWithMultiplePushedBranchWorks()
        {
            RulePredecessor desc = new RulePredecessor
            {
                RuleID = 'F',
                // Reversed,  because that's how it's going to be read in by the parser
                PreCondition = "FFf".Reverse().ToAtoms()
            };

            EvalContext ctx = new EvalContext
            {
                Current = 'F',
                // Reversed, because that's how the LSystem is expected to present it
                Left = "FF[[A]B]f".Reverse().ToAtoms()
            };

            Assert.IsTrue(desc.Match(ctx));
        }

        [TestCase("FF[A]f", "FF[A[B]]f")]
        [TestCase("FF[A]f", "FF[G[B]A]f")]
        public void MatchWithPreconditionWithMultiplePushedBranchAndBranchMatchWorks(string preCondition, string leftContext)
        {
            RulePredecessor desc = new RulePredecessor
            {
                RuleID = 'F',
                // Reversed,  because that's how it's going to be read in by the parser
                PreCondition = preCondition.Reverse().ToAtoms()
            };

            EvalContext ctx = new EvalContext
            {
                Current = 'F',
                // Reversed, because that's how the LSystem is expected to present it
                Left = leftContext.Reverse().ToAtoms()
            };

            Assert.IsTrue(desc.Match(ctx));
        }

        [Test]
        public void MatchWithPostconditionWithMultiplePushedBranchWorks()
        {
            RulePredecessor desc = new RulePredecessor
            {
                RuleID = 'F',
                PostCondition = "FFf".ToAtoms()
            };

            EvalContext ctx = new EvalContext
            {
                Current = 'F',
                Right = "FF[[A]B]f".ToAtoms()
            };

            Assert.IsTrue(desc.Match(ctx));
        }

        [TestCase("FF[A]f", "FF[A[B]]f")]
        [TestCase("FF[A]f", "FF[A[B]G]f")]
        public void MatchWithPostconditionWithMultiplePushedBranchAndBranchMatchWorks(string postCondition, string rightContext)
        {
            RulePredecessor desc = new RulePredecessor
            {
                RuleID = 'F',
                PostCondition = postCondition.ToAtoms()
            };

            EvalContext ctx = new EvalContext
            {
                Current = 'F',
                Right = rightContext.ToAtoms()
            };

            Assert.IsTrue(desc.Match(ctx));
        }

        [TestCase("FF[A]f", "F-F[A-[B]]f+")]
        [TestCase("FF[A]f", "F-F[G-[B]A-]f+")]
        public void MatchWithPreconditionWithMultiplePushedBranchAndIgnoresAndBranchMatchWorks(string preCondition, string leftContext)
        {
            RulePredecessor desc = new RulePredecessor
            {
                RuleID = 'F',
                // Reversed,  because that's how it's going to be read in by the parser
                PreCondition = preCondition.Reverse().ToAtoms()
            };

            EvalContext ctx = new EvalContext
            {
                Current = 'F',
                // Reversed, because that's how the LSystem is expected to present it
                Left = leftContext.Reverse().ToAtoms()
            };

            Assert.IsTrue(desc.Match(ctx, "+-".ToAtoms()));
        }

        [TestCase("FF[A]f", "F-F[A-[B]]f+")]
        [TestCase("FF[A]f", "F-F[A-[B]G-]f+")]
        public void MatchWithPostconditionWithMultiplePushedBranchAndIgnoresAndBranchMatchWorks(string postCondition, string rightContext)
        {
            RulePredecessor desc = new RulePredecessor
            {
                RuleID = 'F',
                PostCondition = postCondition.ToAtoms()
            };

            EvalContext ctx = new EvalContext
            {
                Current = 'F',
                Right = rightContext.ToAtoms()
            };

            Assert.IsTrue(desc.Match(ctx, "+-".ToAtoms()));
        }

        [Test]
        public void MatchABOPExample()
        {
            {
                // From The Algorithmic Beauty of Plants, p. 32
                RulePredecessor desc = new RulePredecessor
                {
                    RuleID = 'S',
                    PreCondition = "BC".Reverse().ToAtoms(),
                    PostCondition = "G[H]M".ToAtoms()
                };

                EvalContext ctx = new EvalContext
                {
                    Current = 'S',
                    Left = "ABC[DE][".Reverse().ToAtoms(),
                    Right = "G[HI[JK]L]MNO]".ToAtoms()
                };

                Assert.IsTrue(desc.Match(ctx, "+-".ToAtoms()));
            }

            {
                // Reverse from The Algorithmic Beauty of Plants, p. 32
                RulePredecessor desc = new RulePredecessor
                {
                    RuleID = 'S',
                    PreCondition = "M[H]G".Reverse().ToAtoms(),
                    PostCondition = "CB".ToAtoms()
                };

                EvalContext ctx = new EvalContext
                {
                    Current = 'S',
                    Left = "ONM[L[KJ]IH]G".Reverse().ToAtoms(),
                    Right = "[ED]CBA".ToAtoms()
                };

                Assert.IsTrue(desc.Match(ctx, "+-".ToAtoms()));
            }
        }
    }
}
