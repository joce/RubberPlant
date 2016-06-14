// Parser inspired from Jonathan Feinberg's work on javalin (https://github.com/jdf/javalin)

parser grammar LSystemParser ;

options {
    tokenVocab = LSystemLexer ;
}

lSystemDefinitions : lSystem+ EOF ;

lSystem : LSYSTEM ID_NAME '{' statement* '}' ;

statement : angle_stmt
          | action_stmt
          | ignore_stmt
          | axiom_stmt
          | rule_stmt ;

angle_stmt : ANGLE_DEF START_ANGLE ANGLE_VALUE END_ANGLE ;

action_stmt : ACTION_DEF START_ACTION ACTION_RULE_ID (ACTION_SEPARATOR ACTION_RULE_ID)* ACTION_DEFINER ACTION END_ACTION ;

ignore_stmt : IGNORE_DEF START_IGNORE IGNORE_RULE_ID+ END_IGNORE ;

axiom_stmt : AXIOM_DEF START_RULE successor END_RULE ;

rule_stmt : RULE_DEF START_RULE rule_predecessor (basic_successor | stochastic_successor) END_RULE ;

rule_predecessor : pre_cond? RULE_ID post_cond? ;

pre_cond : RULE_ID+ '<' ;

post_cond : '>' RULE_ID+ ;

basic_successor : RULE_DEFINER successor ;

stochastic_successor : stochastic_successor_part (RULE_SEPARATOR stochastic_successor_part)* ;

stochastic_successor_part : STOCHASTIC_WEIGHT RULE_DEFINER successor ;

successor : RULE_ID* ;
