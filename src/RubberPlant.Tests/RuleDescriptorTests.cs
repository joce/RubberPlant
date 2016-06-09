using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace RubberPlant.Tests
{
    [TestFixture]
    public class RuleDescriptorTests
    {
        [Test]
        public void RuleToStringPrintsAsExpectedWithoutPreOrPostCondition()
        {
            // Upper case
            for (int i = 0; i < 26; i++)
            {
                char ruleID = (char)('A' + i);
                RuleDescriptor r = new RuleDescriptor {RuleID = ruleID};
                Assert.AreEqual(ruleID.ToString(), r.ToString());
            }

            // Lower case
            for (int i = 0; i < 26; i++)
            {
                char ruleID = (char)('a' + i);
                RuleDescriptor r = new RuleDescriptor {RuleID = ruleID};
                Assert.AreEqual(ruleID.ToString(), r.ToString());
            }

            // Legal special characters
            foreach (var turtleCommand in LSystem.k_implicitTurtleCommands.Keys)
            {
                char ruleID = turtleCommand.RuleName;
                RuleDescriptor r = new RuleDescriptor {RuleID = ruleID};
                Assert.AreEqual(ruleID.ToString(), r.ToString());
            }
        }

        [Test]
        public void RuleToStringPrintsAsExpectedWithPreCondition()
        {
            RuleDescriptor r = new RuleDescriptor {RuleID = 'A', PreCondition = "BBa]A".ToAtoms()};
            Assert.AreEqual("BBa]A < A", r.ToString());
        }

        [Test]
        public void RuleToStringPrintsAsExpectedWithPostCondition()
        {
            RuleDescriptor r = new RuleDescriptor {RuleID = 'A', PostCondition = "BBa]A".ToAtoms()};
            Assert.AreEqual("A > BBa]A", r.ToString());
        }

        [Test]
        public void RuleToStringPrintsAsExpectedWithPreOrPostCondition()
        {
            RuleDescriptor r = new RuleDescriptor
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
            RuleDescriptor rA = new RuleDescriptor
            {
                RuleID = 'A',
                // Reversed,  because that's how it's going to be read in by the parser
                PreCondition = "FFf}B".Reverse().ToAtoms(),
                PostCondition = "BBa]A".ToAtoms()
            };

            // Same as rA
            RuleDescriptor rA1 = new RuleDescriptor
            {
                RuleID = 'A',
                // Reversed,  because that's how it's going to be read in by the parser
                PreCondition = "FFf}B".Reverse().ToAtoms(),
                PostCondition = "BBa]A".ToAtoms()
            };

            // Same as rA, but no precondition
            RuleDescriptor rAPre = new RuleDescriptor
            {
                RuleID = 'A',
                // Reversed,  because that's how it's going to be read in by the parser
                PreCondition = "FFf}B".Reverse().ToAtoms(),
            };

            // Same as rA, but no post-condition
            RuleDescriptor rAPost = new RuleDescriptor
            {
                RuleID = 'A',
                PostCondition = "BBa]A".ToAtoms()
            };

            // Same as rA, but different pre condition
            RuleDescriptor rADiffPre = new RuleDescriptor
            {
                RuleID = 'A',
                // Reversed,  because that's how it's going to be read in by the parser
                PreCondition = "FFf}".Reverse().ToAtoms(),
                PostCondition = "BBa]A".ToAtoms()
            };

            // Same as rA, but different post-condition
            RuleDescriptor rADiffPost = new RuleDescriptor
            {
                RuleID = 'A',
                // Reversed,  because that's how it's going to be read in by the parser
                PreCondition = "FFf}B".Reverse().ToAtoms(),
                PostCondition = "Ba]A".ToAtoms()
            };

            // Same rule as rA, but no pre or post-condition
            RuleDescriptor rAPlain = new RuleDescriptor
            {
                RuleID = 'A'
            };

            // Same pre and post-condition as rA, but different rule ID
            RuleDescriptor rB = new RuleDescriptor
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
            RuleDescriptor desc = new RuleDescriptor {RuleID = 'F'};
            Context ctx = new Context
            {
                Current = 'F',
                // Reversed, because that's how the LSystem is expected to present it
                Predecessors = "FFf}B".Reverse().ToAtoms(),
                Successors = "BBa]A".ToAtoms()
            };

            Assert.IsTrue(desc.Match(ctx));
        }

        [Test]
        public void SimpleNonMatchWorks()
        {
            RuleDescriptor desc = new RuleDescriptor {RuleID = 'F'};
            Context ctx = new Context
            {
                Current = 'B',
                // Reversed, because that's how the LSystem is expected to present it
                Predecessors = "FFf}B".Reverse().ToAtoms(),
                Successors = "BBa]A".ToAtoms()
            };

            Assert.IsFalse(desc.Match(ctx));
        }

        [Test]
        public void MatchWithSimplePreconditionWorks()
        {
            RuleDescriptor desc = new RuleDescriptor
            {
                RuleID = 'F',
                // Reversed,  because that's how it's going to be read in by the parser
                PreCondition = "FFf-".Reverse().ToAtoms()
            };

            Context ctx = new Context
            {
                Current = 'F',
                // Reversed, because that's how the LSystem is expected to present it
                Predecessors = "BFFf-".Reverse().ToAtoms(),
                Successors = "BBa]A".ToAtoms()
            };

            Assert.IsTrue(desc.Match(ctx));
        }

        [Test]
        public void NonMatchWithSimplePreconditionWorks()
        {
            RuleDescriptor desc = new RuleDescriptor
            {
                RuleID = 'F',
                // Reversed,  because that's how it's going to be read in by the parser
                PreCondition = "FB".Reverse().ToAtoms()
            };

            Context ctx = new Context
            {
                Current = 'F',
                // Reversed, because that's how the LSystem is expected to present it
                Predecessors = "FFf}B".Reverse().ToAtoms(),
                Successors = "BBa]A".ToAtoms()
            };

            Assert.IsFalse(desc.Match(ctx));
        }

        [Test]
        public void MatchWithSimplePostconditionWorks()
        {
            RuleDescriptor desc = new RuleDescriptor
            {
                RuleID = 'F',
                PostCondition = "BB+".ToAtoms()
            };

            Context ctx = new Context
            {
                Current = 'F',
                // Reversed, because that's how the LSystem is expected to present it
                Predecessors = "FFf}B".Reverse().ToAtoms(),
                Successors = "BB+]A".ToAtoms()
            };

            Assert.IsTrue(desc.Match(ctx));
        }

        [Test]
        public void NonMatchWithSimplePostconditionWorks()
        {
            RuleDescriptor desc = new RuleDescriptor
            {
                RuleID = 'F',
                PostCondition = "FBf".ToAtoms()
            };

            Context ctx = new Context
            {
                Current = 'F',
                // Reversed, because that's how the LSystem is expected to present it
                Predecessors = "FFf}B".Reverse().ToAtoms(),
                Successors = "BBa]A".ToAtoms()
            };

            Assert.IsFalse(desc.Match(ctx));
        }

        [Test]
        public void MatchWithSimpleConditionsWorks()
        {
            RuleDescriptor desc = new RuleDescriptor
            {
                RuleID = 'F',
                // Reversed,  because that's how it's going to be read in by the parser
                PreCondition = "FFf+".Reverse().ToAtoms(),
                PostCondition = "BBa".ToAtoms()
            };

            Context ctx = new Context
            {
                Current = 'F',
                // Reversed, because that's how the LSystem is expected to present it
                Predecessors = "BFFf+".Reverse().ToAtoms(),
                Successors = "BBa]A".ToAtoms()
            };

            Assert.IsTrue(desc.Match(ctx));
        }

        [Test]
        public void NonMatchWithSimpleConditionsWorks()
        {
            RuleDescriptor desc = new RuleDescriptor
            {
                RuleID = 'F',
                // Reversed,  because that's how it's going to be read in by the parser
                PreCondition = "FFf}".Reverse().ToAtoms(),
                PostCondition = "FBf".ToAtoms()
            };

            Context ctx = new Context
            {
                Current = 'F',
                // Reversed, because that's how the LSystem is expected to present it
                Predecessors = "FFf}B".Reverse().ToAtoms(),
                Successors = "BBa]A".ToAtoms()
            };

            Assert.IsFalse(desc.Match(ctx));
        }

        [Test]
        public void MatchWithPredecessorShorterThanPreconditionFails()
        {
            RuleDescriptor desc = new RuleDescriptor
            {
                RuleID = 'F',
                // Reversed,  because that's how it's going to be read in by the parser
                PreCondition = "FFf}".Reverse().ToAtoms()
            };

            Context ctx = new Context
            {
                Current = 'F',
                // Reversed, because that's how the LSystem is expected to present it
                Predecessors = "FFf".Reverse().ToAtoms()
            };

            Assert.IsFalse(desc.Match(ctx));
        }

        [Test]
        public void MatchWithSuccessorShorterThanPostconditionFails()
        {
            RuleDescriptor desc = new RuleDescriptor
            {
                RuleID = 'F',
                PostCondition = "BBf".ToAtoms()
            };

            Context ctx = new Context
            {
                Current = 'F',
                Successors = "BB".ToAtoms()
            };

            Assert.IsFalse(desc.Match(ctx));
        }

        [Test]
        public void MatchWithIgnoresInPreConditionWorks()
        {
            RuleDescriptor desc = new RuleDescriptor
            {
                RuleID = 'F',
                // Reversed,  because that's how it's going to be read in by the parser
                PreCondition = "FFf".Reverse().ToAtoms()
            };

            Context ctx = new Context
            {
                Current = 'F',
                // Reversed, because that's how the LSystem is expected to present it
                Predecessors = "-F-+Ff".Reverse().ToAtoms()
            };

            IList<Atom> ignores = "-+".ToAtoms();
            Assert.IsTrue(desc.Match(ctx, ignores));
        }

        [Test]
        public void MatchWithIgnoresInPostConditionWorks()
        {
            RuleDescriptor desc = new RuleDescriptor
            {
                RuleID = 'F',
                PostCondition = "FFf".ToAtoms()
            };

            Context ctx = new Context
            {
                Current = 'F',
                Successors = "-F-+Ff".ToAtoms()
            };

            IList<Atom> ignores = "-+".ToAtoms();
            Assert.IsTrue(desc.Match(ctx, ignores));
        }

        [Test]
        public void MatchWithPreconditionWithSimplePushedBranchWorks()
        {
            RuleDescriptor desc = new RuleDescriptor
            {
                RuleID = 'F',
                // Reversed,  because that's how it's going to be read in by the parser
                PreCondition = "FFf".Reverse().ToAtoms()
            };

            Context ctx = new Context
            {
                Current = 'F',
                // Reversed, because that's how the LSystem is expected to present it
                Predecessors = "FF[AB]f".Reverse().ToAtoms()
            };

            Assert.IsTrue(desc.Match(ctx));
        }

        [Test]
        public void MatchWithPreconditionWithSimplePushedBranchAndBranchMatchWorks()
        {
            RuleDescriptor desc = new RuleDescriptor
            {
                RuleID = 'F',
                // Reversed,  because that's how it's going to be read in by the parser
                PreCondition = "FF[A]f".Reverse().ToAtoms()
            };

            Context ctx = new Context
            {
                Current = 'F',
                // Reversed, because that's how the LSystem is expected to present it
                Predecessors = "FF[BA]f".Reverse().ToAtoms()
            };

            Assert.IsTrue(desc.Match(ctx));
        }

        [Test]
        public void MatchWithPostconditionWithSimplePushedBranchWorks()
        {
            RuleDescriptor desc = new RuleDescriptor
            {
                RuleID = 'F',
                PostCondition = "FFf".ToAtoms()
            };

            Context ctx = new Context
            {
                Current = 'F',
                Successors = "FF[AB]f".ToAtoms()
            };

            Assert.IsTrue(desc.Match(ctx));
        }

        [Test]
        public void MatchWithPostconditionWithSimplePushedBranchAndBranchMatchWorks()
        {
            RuleDescriptor desc = new RuleDescriptor
            {
                RuleID = 'F',
                PostCondition = "FF[A]f".ToAtoms()
            };

            Context ctx = new Context
            {
                Current = 'F',
                Successors = "FF[AB]f".ToAtoms()
            };

            Assert.IsTrue(desc.Match(ctx));
        }

        [Test]
        public void MatchWithPreconditionWithMultiplePushedBranchWorks()
        {
            RuleDescriptor desc = new RuleDescriptor
            {
                RuleID = 'F',
                // Reversed,  because that's how it's going to be read in by the parser
                PreCondition = "FFf".Reverse().ToAtoms()
            };

            Context ctx = new Context
            {
                Current = 'F',
                // Reversed, because that's how the LSystem is expected to present it
                Predecessors = "FF[[A]B]f".Reverse().ToAtoms()
            };

            Assert.IsTrue(desc.Match(ctx));
        }

        [TestCase("FF[A]f", "FF[A[B]]f")]
        [TestCase("FF[A]f", "FF[G[B]A]f")]
        public void MatchWithPreconditionWithMultiplePushedBranchAndBranchMatchWorks(string preCondition, string predecessors)
        {
            RuleDescriptor desc = new RuleDescriptor
            {
                RuleID = 'F',
                // Reversed,  because that's how it's going to be read in by the parser
                PreCondition = preCondition.Reverse().ToAtoms()
            };

            Context ctx = new Context
            {
                Current = 'F',
                // Reversed, because that's how the LSystem is expected to present it
                Predecessors = predecessors.Reverse().ToAtoms()
            };

            Assert.IsTrue(desc.Match(ctx));
        }

        [Test]
        public void MatchWithPostconditionWithMultiplePushedBranchWorks()
        {
            RuleDescriptor desc = new RuleDescriptor
            {
                RuleID = 'F',
                PostCondition = "FFf".ToAtoms()
            };

            Context ctx = new Context
            {
                Current = 'F',
                Successors = "FF[[A]B]f".ToAtoms()
            };

            Assert.IsTrue(desc.Match(ctx));
        }

        [TestCase("FF[A]f", "FF[A[B]]f")]
        [TestCase("FF[A]f", "FF[A[B]G]f")]
        public void MatchWithPostconditionWithMultiplePushedBranchAndBranchMatchWorks(string postCondition, string successors)
        {
            RuleDescriptor desc = new RuleDescriptor
            {
                RuleID = 'F',
                PostCondition = postCondition.ToAtoms()
            };

            Context ctx = new Context
            {
                Current = 'F',
                Successors = successors.ToAtoms()
            };

            Assert.IsTrue(desc.Match(ctx));
        }

        [TestCase("FF[A]f", "F-F[A-[B]]f+")]
        [TestCase("FF[A]f", "F-F[G-[B]A-]f+")]
        public void MatchWithPreconditionWithMultiplePushedBranchAndIgnoresAndBranchMatchWorks(string preCondition, string predecessors)
        {
            RuleDescriptor desc = new RuleDescriptor
            {
                RuleID = 'F',
                // Reversed,  because that's how it's going to be read in by the parser
                PreCondition = preCondition.Reverse().ToAtoms()
            };

            Context ctx = new Context
            {
                Current = 'F',
                // Reversed, because that's how the LSystem is expected to present it
                Predecessors = predecessors.Reverse().ToAtoms()
            };

            Assert.IsTrue(desc.Match(ctx, "+-".ToAtoms()));
        }

        [TestCase("FF[A]f", "F-F[A-[B]]f+")]
        [TestCase("FF[A]f", "F-F[A-[B]G-]f+")]
        public void MatchWithPostconditionWithMultiplePushedBranchAndIgnoresAndBranchMatchWorks(string postCondition, string successors)
        {
            RuleDescriptor desc = new RuleDescriptor
            {
                RuleID = 'F',
                PostCondition = postCondition.ToAtoms()
            };

            Context ctx = new Context
            {
                Current = 'F',
                Successors = successors.ToAtoms()
            };

            Assert.IsTrue(desc.Match(ctx, "+-".ToAtoms()));
        }

        [Test]
        public void MatchABOPExample()
        {
            {
                // From The Algorithmic Beauty of Plants, p. 32
                RuleDescriptor desc = new RuleDescriptor
                {
                    RuleID = 'S',
                    PreCondition = "BC".Reverse().ToAtoms(),
                    PostCondition = "G[H]M".ToAtoms()
                };

                Context ctx = new Context
                {
                    Current = 'S',
                    Predecessors = "ABC[DE][".Reverse().ToAtoms(),
                    Successors = "G[HI[JK]L]MNO]".ToAtoms()
                };

                Assert.IsTrue(desc.Match(ctx, "+-".ToAtoms()));
            }

            {
                // Reverse from The Algorithmic Beauty of Plants, p. 32
                RuleDescriptor desc = new RuleDescriptor
                {
                    RuleID = 'S',
                    PreCondition = "M[H]G".Reverse().ToAtoms(),
                    PostCondition = "CB".ToAtoms()
                };

                Context ctx = new Context
                {
                    Current = 'S',
                    Predecessors = "ONM[L[KJ]IH]G".Reverse().ToAtoms(),
                    Successors = "[ED]CBA".ToAtoms()
                };

                Assert.IsTrue(desc.Match(ctx, "+-".ToAtoms()));
            }
        }
    }
}
