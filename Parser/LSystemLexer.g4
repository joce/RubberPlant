// Grammar heavily inspired from Jonathan Feinberg's work on javalin.

lexer grammar LSystemLexer;

// Root
LSYSTEM : 'lsystem' -> pushMode(LSYSTEM_ID_MODE) ;

// Keywords
ANGLE : 'angle' ;
AXIOM : 'axiom' ;
// IGNOTE : 'ignore' ; // for later
RULES : 'rules' ;
// VAR : 'var' ;               // for later
VOCABULARY : 'vocabulary' ;

ACTION : MOVE
       | DRAW
       | NOP ;

// Other symbols
RULE_DEFINER : '->' -> pushMode(RULE_MODE) ;
ACTION_DEFINER : ':' ;
OPEN_BRACE : '{' ;
CLOSE_BRACE : '}' ;
EQUALS : '=' ;

SEMI_COLON : ';' ;

NUMBER : '-'? INT '.' INT EXP? // 1.35, 1.35E-9, 0.3, -4.5
       | '-'? INT EXP // 1e10 -3e4
       | '-'? INT ; // -3, 45

// Rule name (single char)
RULE_ID : [A-Za-z] ;

fragment ID_NAME : ID_LETTER (ID_LETTER | DIGIT)* ;
fragment ID_LETTER : [A-Za-z_] ;
fragment DIGIT : [0-9] ;
fragment INT : '0' | DIGIT DIGIT* ; // no leading zeros
fragment EXP : [Ee] [+\-]? INT ; // \- since - means "range" inside [...]

// Turtle commands
fragment CUT_OFF_BRANCH : '%';
fragment DECR_DIAMETER : '!' ;
fragment END_BRANCH : ']' ;
fragment END_POLY : '}' ;
fragment INCR_COLOR_IDX : '\'';
fragment PITCH_UP : '^' ;
fragment PITCH_DOWN : '&' ;
fragment PREDEFINED_SURF : '~' ;
fragment ROLL_LEFT : '\\' ;
fragment ROLL_RIGHT : '/' ;
fragment RECORD_VERTEX : '.' ;
fragment ROTATE_TO_VERTICAL : '$' ;
fragment START_BRANCH : '[' ;
fragment START_POLY : '{' ;
fragment TURN_AROUND : '|' ;
fragment TURN_LEFT : '+' ;
fragment TURN_RIGHT : '-' ;

// Actions keywords
fragment DRAW : 'draw' ;
fragment MOVE : 'move' ;
fragment NOP : 'nop' ;

// Comment
LINE_COMMENT : '//' .*? '\r'? '\n' -> skip ;
COMMENT : '/*' .*? '*/' -> skip ;

// Whitespace
WS : [ \r\n\t]+ -> skip ;

///////////////////////////////////////////////////////////////////////////////
mode RULE_MODE ;

EQUALS_AXIOM_RULE : '=' ;

RULE_ID_RULE_MODE : RULE_ID ;

TURTLE_CMD : CUT_OFF_BRANCH
           | DECR_DIAMETER
           | END_BRANCH
           | END_POLY
           | INCR_COLOR_IDX
           | PITCH_UP
           | PITCH_DOWN
           | PREDEFINED_SURF
           | ROLL_LEFT
           | ROLL_RIGHT
           | RECORD_VERTEX
           | ROTATE_TO_VERTICAL
           | START_BRANCH
           | START_POLY
           | TURN_AROUND
           | TURN_LEFT
           | TURN_RIGHT ;

SEMI_COLON_END_RULE : ';' -> popMode;

// Comment
LINE_COMMENT_RULE : LINE_COMMENT -> skip ;
COMMENT_RULE : COMMENT -> skip ;

// Whitespace
WS_RULE : WS -> skip ;

///////////////////////////////////////////////////////////////////////////////
mode LSYSTEM_ID_MODE ;

ID_NAME_LSYSTEM : ID_NAME ;
OPEN_BRACE_LSYSTEM : '{' -> popMode ;

// Comment
LINE_COMMENT_LSYSTEM : LINE_COMMENT -> skip ;
COMMENT_LSYSTEM : COMMENT -> skip ;

// Whitespace
WS_LSYSTEM : WS -> skip ;

// Later

// ///////////////////////////////////////////////////////////////////////////////
// mode EXPRESSION ;
//
// ID_NAME_EXP : ID_NAME ;
//
// // Comment
// LINE_COMMENT_EXP : LINE_COMMENT -> skip ;
// COMMENT_EXP : COMMENT -> skip ;
//
// // Whitespace
// WS_EXP : WS -> skip ;
