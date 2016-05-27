// Parser heavily inspired from Jonathan Feinberg's work on javalin (https://github.com/jdf/javalin)

parser grammar LSystemParser;

options {
    tokenVocab = LSystemLexer;
}

lSystemDefinitions : lSystem+ EOF ;

lSystem : LSYSTEM ID_NAME '{' statement* '}' ;

statement : angle_stmt
          | vocabulary_stmt
          | rules_stmt ;

angle_stmt : ANGLE '=' NUMBER SEMI_COLON ;

vocabulary_stmt : VOCABULARY '{' action_stmt* '}' ;

// TODO allow multiple rules on single line, e.g. "A, B, C, D : draw;" or "f, h, y, z : move;"
action_stmt : RULE_ID (SEPARATOR RULE_ID)* ':' ACTION SEMI_COLON ;

rules_stmt : RULES '{' (stochastic_rule_stmt | rule_stmt | axiom_stmt)* '}' ;

axiom_stmt : AXIOM '->' prod_rule SEMI_COLON_END_RULE ;

stochastic_rule_stmt : RULE_ID '{' stochastic_subrule_stmt+ '}';

stochastic_subrule_stmt : NUMBER '->' prod_rule SEMI_COLON_END_RULE ;

rule_stmt : RULE_ID '->' prod_rule SEMI_COLON_END_RULE ;

prod_rule : RULE_ID_RULE_MODE* ;
