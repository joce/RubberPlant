parser grammar LSystemParser;

options {
  tokenVocab = LSystemLexer;
}

lSystemDefinitions : lSystem+ EOF ;

lSystem : LSYSTEM ID_NAME_LSYSTEM OPEN_BRACE_LSYSTEM statement* '}' ;

statement : angle_stmt
          | vocabulary_stmt
          | rules_stmt ;

angle_stmt : ANGLE EQUALS NUMBER SEMI_COLON ;

vocabulary_stmt : VOCABULARY OPEN_BRACE action_stmt* '}' ;

action_stmt : RULE_ID ':' ACTION SEMI_COLON ;

rules_stmt : RULES OPEN_BRACE (rule_stmt | axiom_stmt)* '}' ;

axiom_stmt : AXIOM '->' prod_rule SEMI_COLON_END_RULE ;

rule_stmt : RULE_ID '->' prod_rule SEMI_COLON_END_RULE ;

prod_rule : rule_atom+ ;

rule_atom : RULE_ID_RULE_MODE
          | TURTLE_CMD ;

