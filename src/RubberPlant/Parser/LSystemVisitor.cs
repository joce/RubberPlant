using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RubberPlant
{
    public class LSystemVisitor : LSystemParserBaseVisitor<double>
    {
        public List<LSystem> AllLSystems { get; }

        private LSystem m_currentLSystem;

        private List<Atom> m_currentRuleReplacement;
        private Rule m_currentRule;
        private HashSet<Atom> m_usedRules;

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

            m_currentLSystem.Angle = double.Parse(ctx.ANGLE_VALUE().GetText());
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
            if (m_currentRuleReplacement.Count > 0)
            {
                m_currentRule = new Rule();
                m_currentRule.AddReplacement(m_currentRuleReplacement);
                m_currentLSystem.Axiom = m_currentRule;
                m_hasAxiom = true;
            }
            return 0;
        }

        public override double VisitRule_description(LSystemParser.Rule_descriptionContext ctx)
        {
            VisitChildren(ctx);

            m_currentRule.Descriptor.RuleID = ctx.RULE_ID().GetText()[0];
            if (m_currentLSystem.HasRule(m_currentRule.Descriptor))
            {
                m_errorListener.VisitError(ctx, ErrorLevel.Error, string.Format("LSystem {0} has more than one rule defined for {1}.", m_currentLSystem.Name, m_currentRule.Descriptor.RuleID));
            }

            return 0;
        }

        public override double VisitPre_cond(LSystemParser.Pre_condContext ctx)
        {
            // Precondition is stored backwards for easier matching
            m_currentRule.Descriptor.PreCondition = ctx.RULE_ID().Select(r => new Atom(r.GetText()[0])).Reverse().ToList();

            return 0;
        }

        public override double VisitPost_cond(LSystemParser.Post_condContext ctx)
        {
            m_currentRule.Descriptor.PostCondition = ctx.RULE_ID().Select(r => new Atom(r.GetText()[0])).ToList();

            return 0;
        }

        public override double VisitRule_stmt(LSystemParser.Rule_stmtContext ctx)
        {
            m_currentRule = new Rule();
            VisitChildren(ctx);
            m_currentLSystem.Rules.Add(m_currentRule);

            return 0;
        }

        public override double VisitBasic_rule(LSystemParser.Basic_ruleContext ctx)
        {
            VisitChildren(ctx);

            m_currentRule.AddReplacement(m_currentRuleReplacement);

            return 0;
        }

        public override double VisitStochastic_rule(LSystemParser.Stochastic_ruleContext ctx)
        {
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
                m_errorListener.VisitError(ctx, ErrorLevel.Warning, string.Format("LSystem {0} stochastic rule {1} weights do not total 1. Values will be normalized.", m_currentLSystem.Name, m_currentRule.Descriptor.RuleID));
                m_currentRule.NormalizeWeights();
            }

            return 0;
        }

        public override double VisitStochastic_subrule(LSystemParser.Stochastic_subruleContext ctx)
        {
            float weight = float.Parse(ctx.STOCHASTIC_WEIGHT().GetText());

            // Weight of a stochastic rule can't be 0 or negative. That would make no sense.
            if (weight <= 0)
            {
                m_errorListener.VisitError(ctx, ErrorLevel.Error, string.Format("LSystem {0} stochastic rule {1} has negative or zero value ({2}). This makes no sense.", m_currentLSystem.Name, m_currentRule.Descriptor.RuleID, weight));
                return 0;
            }

            VisitChildren(ctx);

            m_currentRule.AddReplacement(m_currentRuleReplacement, weight);

            return 0;
        }

        public override double VisitProd_rule(LSystemParser.Prod_ruleContext ctx)
        {
            if (ctx.RULE_ID() != null)
            {
                m_currentRuleReplacement = ctx.RULE_ID().Select(r => new Atom(r.GetText()[0])).ToList();
                foreach (var atom in m_currentRuleReplacement)
                {
                    m_usedRules.Add(atom);
                }
            }

            return VisitChildren(ctx);
        }

        public override double VisitAction_stmt(LSystemParser.Action_stmtContext ctx)
        {
            var rules = ctx.ACTION_RULE_ID().Select(r => r.GetText()[0]).ToArray();

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

        public override double VisitIgnore_stmt(LSystemParser.Ignore_stmtContext ctx)
        {
            var atoms = ctx.IGNORE_RULE_ID().Select(r => r.GetText()[0]).ToArray();
            foreach (var atom in atoms.Select(r => new Atom(r)))
            {
                if (m_currentLSystem.MatchIgnores.Contains(atom))
                {
                    m_errorListener.VisitError(ctx, ErrorLevel.Info, string.Format("LSystem {0} has atom {1} ignored more than once.", m_currentLSystem.Name, atom));
                }
                else if ("[]{}".Contains(atom.RuleName))
                {
                    m_errorListener.VisitError(ctx, ErrorLevel.Error, string.Format("LSystem {0} has branch/polygon atom {1} ignored. This is not supported.", m_currentLSystem.Name, atom));
                    break;
                }
                else
                {
                    m_currentLSystem.MatchIgnores.Add(atom);
                }
            }
            return 0;
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

            // TODO Check that no pre or post condition contain ignored symbols.
            foreach (var rule in m_currentLSystem.Rules)
            {
                var nonDefinedPreCond = rule.Descriptor.PreCondition.Except(m_usedRules).ToList();
                if (nonDefinedPreCond.Any())
                {
                    var nonDefined = string.Join(", ", nonDefinedPreCond);
                    StringBuilder sb = new StringBuilder(string.Format("LSystem {0} rule {1} uses undefined symbol(s) {2}.\n", m_currentLSystem.Name, rule.Descriptor.RuleID, nonDefined));
                    sb.Append("This rule will be unreachable.");
                    m_errorListener.VisitError(ctx, ErrorLevel.Warning, sb.ToString());
                }
            }

            return true;
        }
    }
}
