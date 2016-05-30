// Grammar heavily inspired from Jonathan Feinberg's work on javalin (https://github.com/jdf/javalin)

lexer grammar LSystemLexer ;

// Root
LSYSTEM : 'lsystem'  -> pushMode(NAME_MODE) ;

OPEN_BRACE : '{' ;
CLOSE_BRACE : '}' ;

RULE_DEF : ('r' | 'rule') -> pushMode(RULE_MODE) ;
AXIOM_DEF : ('x' | 'ax' | 'axiom') -> pushMode(RULE_MODE) ;
ACTION_DEF : ('a' | 'action') -> pushMode(ACTION_MODE) ;
ANGLE_DEF : ('g' | 'angle') -> pushMode(ANGLE_MODE) ;

//DEFINE_DEF : ('d' | 'define') -> pushMode(DEFINE_MODE) ;

NUMBER : '-'? INT '.' INT EXP? // 1.35, 1.35E-9, 0.3, -4.5
       | '-'? INT EXP // 1e10 -3e4
       | '-'? INT ; // -3, 45

fragment ID_LETTER : [A-Za-z_] ;
fragment DIGIT : [0-9] ;
fragment INT : '0' | DIGIT DIGIT* ; // no leading zeros
fragment EXP : [Ee] [+\-]? INT ; // \- since - means "range" inside [...]

// Turtle commands
fragment TURTLE_CMD : '%'   // CUT_OFF_BRANCH
                    | '!'   // DECR_DIAMETER
                    | ']'   // END_BRANCH
                    | '}'   // END_POLY
                    | '\''  // INCR_COLOR_IDX
                    | '^'   // PITCH_UP
                    | '&'   // PITCH_DOWN
                    | '~'   // PREDEFINED_SURF
                    | '\\'  // ROLL_LEFT
                    | '/'   // ROLL_RIGHT
                    | '.'   // RECORD_VERTEX
                    | '$'   // ROTATE_TO_VERTICAL
                    | '['   // START_BRANCH
                    | '{'   // START_POLY
                    | '|'   // TURN_AROUND
                    | '+'   // TURN_LEFT
                    | '-' ; // TURN_RIGHT


// Comment
LINE_COMMENT : '//' .*? '\r'? '\n' -> skip ;
COMMENT : '/*' .*? '*/' -> skip ;

// Whitespace
WS : [ \r\n\t]+ -> skip ;


///////////////////////////////////////////////////////////////////////////////
mode NAME_MODE ;

ID_NAME : ID_LETTER (ID_LETTER | DIGIT)*  -> popMode ;

// Comment
LINE_COMMENT_NAME : LINE_COMMENT -> skip ;
COMMENT_NAME : COMMENT -> skip ;

// Whitespace
WS_NAME: WS -> skip ;


///////////////////////////////////////////////////////////////////////////////
mode RULE_MODE ;

START_RULE : ':' ;
RULE_SEPARATOR : ',' ;
END_RULE : ';' -> popMode ;
RULE_DEFINER : '->' ;
RULE_PRED : '<' ;
RULE_POST : '>' ;

RULE_ID : [A-Za-z]
        | TURTLE_CMD ;

STOCHASTIC_WEIGHT : NUMBER ;

// Comment
LINE_COMMENT_RULE : LINE_COMMENT -> skip ;
COMMENT_RULE : COMMENT -> skip ;

// Whitespace
WS_RULE : WS -> skip ;


///////////////////////////////////////////////////////////////////////////////
mode ACTION_MODE ;

START_ACTION : ':' ;
ACTION_SEPARATOR : ',' ;
END_ACTION : ';' -> popMode ;
ACTION_DEFINER : '->' ;

ACTION_RULE_ID : [A-Za-z] ;

ACTION : 'move'
       | 'draw'
       | 'nop' ;

// Comment
LINE_COMMENT_ACTION : LINE_COMMENT -> skip ;
COMMENT_ACTION : COMMENT -> skip ;

// Whitespace
WS_ACTION : WS -> skip ;


///////////////////////////////////////////////////////////////////////////////
mode ANGLE_MODE ;

START_ANGLE : ':' ;
END_ANGLE : ';' -> popMode ;

ANGLE_VALUE : NUMBER ;

// Comment
LINE_COMMENT_ANGLE : LINE_COMMENT -> skip ;
COMMENT_ANGLE : COMMENT -> skip ;

// Whitespace
WS_ANGLE : WS -> skip ;
