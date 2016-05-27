using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RubberPlant
{
    public class LSystemVisitor : LSystemParserBaseVisitor<double>
    {
        public List<LSystem> AllLSystems { get; }

        private LSystem m_currentLSystem;

        private List<Atom> m_currentRuleBody;
        private Rule m_currentRule;
        private HashSet<Atom> m_usedRules;
        private char m_currentlRuleName;

        // Validation states
        private bool m_hasAngle;
        private bool m_hasAxiom;

        private readonly LSystemErrorListener m_errorListener;

        public LSystemVisitor(LSystemErrorListener errorListener)
        {
            AllLSystems = new List<LSystem>();
            m_errorListener = errorListener;
            m_usedRules = new HashSet<Atom>();
        }

        public override double VisitLSystem(LSystemParser.LSystemContext ctx)
        {
            m_currentLSystem = new LSystem {Name = ctx.ID_NAME().GetText()};
            m_usedRules = new HashSet<Atom>();
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

            VisitChildren(ctx);
            if (m_currentRuleBody.Count > 0)
            {
                m_currentRule = new Rule();
                m_currentRule.AddBody(m_currentRuleBody);
                m_currentLSystem.Axiom = m_currentRule;
                m_hasAxiom = true;
            }
            return 0;
        }

        public override double VisitRule_stmt(LSystemParser.Rule_stmtContext ctx)
        {
            Atom atom = new Atom(ctx.RULE_ID().GetText()[0]);
            if (m_currentLSystem.HasRule(atom))
            {
                m_errorListener.VisitError(ctx, ErrorLevel.Error, string.Format("LSystem {0} has more than one rule defined for {1}.", m_currentLSystem.Name, atom.RuleName));
                return 0;
            }

            VisitChildren(ctx);

            m_currentRule = new Rule {RuleID = atom};
            m_currentRule.AddBody(m_currentRuleBody);
            m_currentLSystem.Rules.Add(m_currentRule);

            return 0;
        }

        public override double VisitStochastic_rule_stmt(LSystemParser.Stochastic_rule_stmtContext ctx)
        {
            Atom atom = new Atom(ctx.RULE_ID().GetText()[0]);
            if (m_currentLSystem.HasRule(atom))
            {
                m_errorListener.VisitError(ctx, ErrorLevel.Error, string.Format("LSystem {0} has more than one rule defined for {1}.", m_currentLSystem.Name, atom.RuleName));
                return 0;
            }

            m_currentRule = new Rule {RuleID = atom};
            m_currentlRuleName = atom.RuleName;

            VisitChildren(ctx);

            // Don't bother continuing if we found something bad in our children.
            if (m_errorListener.HasAnySerious)
            {
                return 0;
            }

            // Check if the weights add up to 1.0 precisely. If not, no sweat. We will normalize.
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (m_currentRule.TotalWeight != 1.0f)
            {
                m_errorListener.VisitError(ctx, ErrorLevel.Warning, string.Format("LSystem {0} stochastic rule {1} weights do not total 1. Values will be normalized.", m_currentLSystem.Name, atom.RuleName));
                m_currentRule.NormalizeWeights();
            }

            m_currentLSystem.Rules.Add(m_currentRule);

            return 0;
        }

        public override double VisitStochastic_subrule_stmt(LSystemParser.Stochastic_subrule_stmtContext ctx)
        {
            float weight = float.Parse(ctx.NUMBER().GetText());

            // Weight of a stochastic rule can't be 0 or negative. That would make no sense.
            if (weight <= 0)
            {
                m_errorListener.VisitError(ctx, ErrorLevel.Error, string.Format("LSystem {0} stochastic rule {1} has negative or zero value ({2}). This makes no sense.", m_currentLSystem.Name, m_currentlRuleName, weight));
                return 0;
            }

            VisitChildren(ctx);

            m_currentRule.AddBody(m_currentRuleBody, weight);

            return 0;
        }

        public override double VisitProd_rule(LSystemParser.Prod_ruleContext ctx)
        {
            if (ctx.RULE_ID_RULE_MODE() != null)
            {
                m_currentRuleBody = ctx.RULE_ID_RULE_MODE().Select(r => new Atom(r.GetText()[0])).ToList();
                foreach (var atom in m_currentRuleBody)
                {
                    m_usedRules.Add(atom);
                }
            }

            return VisitChildren(ctx);
        }

        public override double VisitAction_stmt(LSystemParser.Action_stmtContext ctx)
        {
            var rules = ctx.RULE_ID().Select(r => r.GetText()[0]).ToArray();

            HashSet<char> extraDefined = new HashSet<char>();

            // Check for duplicates in the current list.
            foreach (var extra in rules.GroupBy(c => c, (c, g) => new {cnt = g.Count(), val = c}).Where(v => v.cnt > 1).Select(v => v.val))
            {
                extraDefined.Add(extra);
            }

            rules = rules.Distinct().ToArray();

            foreach (var rule in rules)
            {
                // Check for duplicates elsewhere in the vocabulary.
                if (m_currentLSystem.Vocabulary.ContainsKey(rule))
                {
                    extraDefined.Add(rule);
                }
            }

            if (extraDefined.Any())
            {
                foreach (var rule in extraDefined)
                {
                    m_errorListener.VisitError(ctx, ErrorLevel.Error, string.Format("LSystem {0} has action defined for rule {1} more than once.", m_currentLSystem.Name, rule));
                }
                return 0;
            }

            foreach (var rule in rules)
            {
                m_currentLSystem.Vocabulary[rule] = ctx.ACTION().GetText().ToCommand();
            }

            return VisitChildren(ctx);
        }

        private bool ValidateLSystem(LSystemParser.LSystemContext ctx)
        {
            // TODO Need to check for "orphan" rules and warn about them (warning, set to NOP).

            // TODO Need to check for "semi- orphan" rules (rules used only within themselves) and warn about them (info, strip rules).

            // No axiom is bad.
            if (!m_hasAxiom)
            {
                m_errorListener.VisitError(ctx, ErrorLevel.Error, string.Format("LSystem {0} has no defined axiom and therefore can't be loaded.", m_currentLSystem.Name));
                return false;
            }

            // No angle... we can kinda deal with that.
            if (!m_hasAngle)
            {
                m_errorListener.VisitError(ctx, ErrorLevel.Warning, string.Format("LSystem {0} has no defined angle. Defaulting to 90 degrees.", m_currentLSystem.Name));
                m_currentLSystem.Angle = 90;
            }

            // Rules without actions defined in the vocabulary will be set to NOP.
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
