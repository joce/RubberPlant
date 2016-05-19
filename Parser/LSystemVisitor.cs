using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RubberPlant
{
    public class LSystemVisitor : LSystemParserBaseVisitor<double>
    {
        public List<LSystem> AllLSystems { get; }

        private LSystem m_currentLSystem;
        private List<Atom> m_currentRule;
        private List<Tuple<float, List<Atom>>> m_currentStochasticRule;
        private HashSet<IDAtom> m_usedRules;
        private char m_currentlRuleName;

        // Validation states
        private bool m_hasAngle;
        private bool m_hasAxiom;

        private readonly LSystemErrorListener m_errorListener;

        public LSystemVisitor(LSystemErrorListener errorListener)
        {
            AllLSystems = new List<LSystem>();
            m_errorListener = errorListener;
            m_usedRules = new HashSet<IDAtom>();
        }

        public override double VisitLSystem(LSystemParser.LSystemContext ctx)
        {
            m_currentLSystem = new LSystem {Name = ctx.ID_NAME_LSYSTEM().GetText()};
            m_usedRules = new HashSet<IDAtom>();
            m_hasAngle = false;
            m_hasAxiom = false;
            VisitChildren(ctx);

            if (!m_errorListener.HasErrors)
            {
                if (!ValidateLSystem(ctx))
                {
                    return 0;
                }

                AllLSystems.Add(m_currentLSystem);
            }

            return 0;
        }

        public override double VisitAngle_stmt(LSystemParser.Angle_stmtContext ctx)
        {
            if (m_hasAngle)
            {
                m_errorListener.VisitError(ctx, ErrorLevel.Error, string.Format("LSystem {0} has more than one angle defined.", m_currentLSystem.Name));
                return 0;
            }

            m_currentLSystem.Angle = double.Parse(ctx.NUMBER().GetText());
            m_hasAngle = true;
            return VisitChildren(ctx);
        }

        public override double VisitAxiom_stmt(LSystemParser.Axiom_stmtContext ctx)
        {
            if (m_hasAxiom)
            {
                m_errorListener.VisitError(ctx, ErrorLevel.Error, string.Format("LSystem {0} has more than one axiom defined.", m_currentLSystem.Name));
                return 0;
            }

            m_currentRule = new List<Atom>();
            VisitChildren(ctx);
            if (m_currentRule.Count > 0)
            {
                m_currentLSystem.Axiom = m_currentRule;
                m_hasAxiom = true;
            }
            return 0;
        }

        public override double VisitRule_stmt(LSystemParser.Rule_stmtContext ctx)
        {
            IDAtom idAtom = new IDAtom(ctx.RULE_ID().GetText()[0]);
            if (m_currentLSystem.HasRule(idAtom))
            {
                m_errorListener.VisitError(ctx, ErrorLevel.Error, string.Format("LSystem {0} has more than one rule defined for {1}.", m_currentLSystem.Name, idAtom.RuleName));
                return 0;
            }
            m_currentRule = new List<Atom>();
            VisitChildren(ctx);

            m_currentLSystem.Rules[idAtom] = m_currentRule;

            return 0;
        }

        public override double VisitStochastic_rule_stmt(LSystemParser.Stochastic_rule_stmtContext ctx)
        {
            IDAtom idAtom = new IDAtom(ctx.RULE_ID().GetText()[0]);
            if (m_currentLSystem.HasRule(idAtom))
            {
                m_errorListener.VisitError(ctx, ErrorLevel.Error, string.Format("LSystem {0} has more than one rule defined for {1}.", m_currentLSystem.Name, idAtom.RuleName));
                return 0;
            }

            m_currentStochasticRule = new List<Tuple<float, List<Atom>>>();
            m_currentlRuleName = idAtom.RuleName;

            VisitChildren(ctx);

            // Don't bother continuing if we found something bad in our children.
            if (m_errorListener.HasAnySerious)
            {
                return 0;
            }

            float totalWeight = m_currentStochasticRule.Select(r => r.Item1).Sum();
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (totalWeight != 1.0f)
            {
                m_errorListener.VisitError(ctx, ErrorLevel.Warning, string.Format("LSystem {0} stochastic rule {1} weights do not total 1. Values will be normalized.", m_currentLSystem.Name, idAtom.RuleName));

                var normalizedStochasticRule = new List<Tuple<float, List<Atom>>>();
                foreach (var subrule in m_currentStochasticRule)
                {
                    normalizedStochasticRule.Add(new Tuple<float, List<Atom>>(subrule.Item1/totalWeight, subrule.Item2)); ;
                }
                m_currentStochasticRule = normalizedStochasticRule;
            }

            m_currentLSystem.StochasticRules[idAtom] = m_currentStochasticRule;

            return 0;
        }

        public override double VisitStochastic_subrule_stmt(LSystemParser.Stochastic_subrule_stmtContext ctx)
        {
            float weight = float.Parse(ctx.NUMBER().GetText());
            if (weight <= 0)
            {
                m_errorListener.VisitError(ctx, ErrorLevel.Error, string.Format("LSystem {0} stochastic rule {1} has negative or zero value ({2}). This makes no sense.", m_currentLSystem.Name, m_currentlRuleName, weight));
                return 0;
            }

            m_currentRule = new List<Atom>();

            VisitChildren(ctx);

            m_currentStochasticRule.Add(new Tuple<float, List<Atom>>(weight, m_currentRule));

            return 0;
        }

        public override double VisitRule_atom(LSystemParser.Rule_atomContext ctx)
        {
            if (ctx.RULE_ID_RULE_MODE() != null)
            {
                var atom = new IDAtom(ctx.RULE_ID_RULE_MODE().GetText()[0]);
                m_currentRule.Add(atom);
                m_usedRules.Add(atom);
            }
            else
            {
                m_currentRule.Add(new TurtleAtom(ctx.TURTLE_CMD().GetText()[0]));
            }

            return VisitChildren(ctx);
        }

        public override double VisitAction_stmt(LSystemParser.Action_stmtContext ctx)
        {
            var rule = new IDAtom(ctx.RULE_ID().GetText()[0]);

            if (m_currentLSystem.Vocabulary.ContainsKey(rule))
            {
                m_errorListener.VisitError(ctx, ErrorLevel.Error, string.Format("LSystem {0} has action defined for rule {1} more than once.", m_currentLSystem.Name, rule.RuleName));
                return 0;
            }

            m_currentLSystem.Vocabulary[rule] = ctx.ACTION().GetText().ToCommand();

            return VisitChildren(ctx);
        }

        private bool ValidateLSystem(LSystemParser.LSystemContext ctx)
        {
            // TODO Need to check for "orphan" rules and warn about them (warning, set to NOP).

            // TODO Need to check for "semi- orphan" rules (rules used only within themselves) and warn about them (info, strip rules).
            if (!m_hasAxiom)
            {
                m_errorListener.VisitError(ctx, ErrorLevel.Error, string.Format("LSystem {0} has no defined axiom and therefore can't be loaded.", m_currentLSystem.Name));
                return false;
            }

            if (!m_hasAngle)
            {
                m_errorListener.VisitError(ctx, ErrorLevel.Warning, string.Format("LSystem {0} has no defined angle. Defaulting to 90 degrees.", m_currentLSystem.Name));
                m_currentLSystem.Angle = 90;
            }

            var rulesWithoutActions = m_usedRules.Except(m_currentLSystem.Vocabulary.Keys).ToArray();
            if (rulesWithoutActions.Length > 0)
            {
                StringBuilder sb = new StringBuilder(string.Format("LSystem {0} has rule(s) unknown to its vocabulary: ", m_currentLSystem.Name));
                for (int i = 0; i < rulesWithoutActions.Length - 1; i++)
                {
                    sb.Append(rulesWithoutActions[i].RuleName);
                    sb.Append(", ");
                    m_currentLSystem.Vocabulary[rulesWithoutActions[i]] = TurtleCommand.Nop;
                }
                sb.Append(rulesWithoutActions[rulesWithoutActions.Length - 1].RuleName);
                sb.Append(". Assuming \"nop\" for every rule.");
                m_currentLSystem.Vocabulary[rulesWithoutActions[rulesWithoutActions.Length - 1]] = TurtleCommand.Nop;

                m_errorListener.VisitError(ctx, ErrorLevel.Warning, sb.ToString());
            }

            return true;
        }
    }
}
