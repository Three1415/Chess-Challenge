using ChessChallenge.API;
using System;
using System.Collections.Generic;
using System.Linq;

public class MyBot : IChessBot
{
    //Bitmasks for, in order, the eval, then the 6th, 5th, 4th, etc.
    //moves returned by AlphaBeta
    static public ulong[] BITMASKS = new ulong[]{
        0xFFFF000000000000,
        0x0000FF0000000000,
        0x000000FF00000000,
        0x00000000FF000000,
        0x0000000000FF0000,
        0x000000000000FF00,
        0x00000000000000FF

    };
    static int searchDepth = 4;
    public bool isBotWhite;
    public Move Think(Board board, Timer timer)
    {   
        isBotWhite = board.IsWhiteToMove;
        ulong test = AlphaBeta(board, 0, UInt64.MaxValue, searchDepth, searchDepth%2==1);
        //Console.WriteLine(Convert.ToString((long) test, 2));

        List<Move> principalVariation = new List<Move>();
        List<int> moveIndices = new List<int>();
        int index = 0;
        for (int i = searchDepth + 1; i>=2; i--) {
            index = (int) (((test << (8 * i)) >> 56) - 1);
            //Console.WriteLine("Index is: " + index.ToString());
            moveIndices.Add(index);
        }
        GrabMoves(board, moveIndices, ref principalVariation, 0);
        
        Console.Write("Principal variation is: ");
        foreach(Move m in principalVariation) {
            Console.Write(m.ToString() + ", ");
        }
        Console.Write("\n");
        
        return principalVariation[0];
    }

    void GrabMoves(Board b, List<int> moveIndices, ref List<Move> principalVariation, int pos) {
        if(pos >= moveIndices.Count) {
            return;
        }
        Move[] moves = GetOrderedLegalMoves(b, b.GetLegalMoves());
        int index = Math.Max(moveIndices[pos], 0);
        if(moves.Length == 0) {
            return;
        }
        Console.WriteLine("Index is: " + index.ToString());
        Move move = moves[index];
        principalVariation.Add(move);
        b.MakeMove(move);
        GrabMoves(b, moveIndices, ref principalVariation, pos + 1);
        b.UndoMove(move);
    }

    Move[] GetOrderedLegalMoves(Board b, Move[] legalMoves) {
        int[] sortTable = new int[legalMoves.Count()];
        int key;
        Move m;
        for(int i = 0; i < sortTable.Count(); i++) {
            m = legalMoves[i];
            b.MakeMove(m);
            /*
            The more of these that are satisfied by a given move, the lower the final key value will be,
            and thus the earlier this move will appear in the sorted array.
            In order, these check for: 
                -Game-ending moves
                -Checks
                -Promotions
                -Captures
                -Evasions
                -Everything else
            */
            key = 0b11111 - (b.IsInCheckmate() || b.IsDraw() ?  0b10000 : 0b100000) - (b.IsInCheck() ? 0b01000 : 0) -  (m.IsPromotion ? 0b00100 : 0) - ( m.IsCapture ? 0b00010 : 0) - (b.SquareIsAttackedByOpponent(m.StartSquare) ? 0b00001 : 0);
            sortTable[i] = key;
            b.UndoMove(m); 
        }
        //Sort the legal moves by each key in the associated sortTable array, in ascending order
        Array.Sort(sortTable, legalMoves);
        return legalMoves;
    }

    /*
    The returned ulong is being used as a "register", with the following structure in bits:
        [0000 0000 0000 0000] [0000 0000] [0000 0000] [0000 0000] [0000 0000] [0000 0000]  [0000 0000]
            Eval (16 bits)       Move 6      Move 5      Move 4      Move 3     Move 2       Move 1
    Each 8-bit move represents the index of that move in the board.getLegalMoves() list. This is
    more efficient than storing the 12-bit move itself, since the maximum number of legal moves
    in any reachable chess position seems to be < 256; thus, we can fit 5 moves in a single ulong.

    This is done for pruning purposes (see below) and to let us return the principal variation
    simultaneously with the eval. Any comparisons of the eval will be faithful to the comparisons
    between the actual eval bits, since the eval is the *first* 16 bits and thus dominates the numerical value
    of the ulong. With this optimization, we can run an alpha-beta search with only one implemented function,
    and don't have to write a separate RootSearch() function either, greatly reducing token usage in exchange
    for making this eldritch abomination.
    */
    ulong AlphaBeta(Board board, ulong alpha, ulong beta, int depthRemaining, bool isMinimizing) {
        //Console.WriteLine("boundingEval is " + boundingEval.ToString());
        Move[] legalMoves = board.GetLegalMoves();

        ulong moveIndex = 0;
        //ulong moveBits;
        ulong currentEval;
        ulong boundingEval;

        if(depthRemaining==0 || legalMoves.Length == 0 || board.IsDraw() || board.IsInCheckmate()){
            //Console.WriteLine("Board evaluation is: " + Convert.ToString(Evaluate(board)));
            //Console.WriteLine("Shifted board evaluation is: " + Convert.ToString((long) (((ulong) Evaluate(board)) << 48), 2));
            return ((ulong) Evaluate(board, depthRemaining)) << 48 ;
        } 

        legalMoves = GetOrderedLegalMoves(board, legalMoves);
        foreach(Move m in legalMoves){
            moveIndex++;
            //Console.WriteLine("Depth remaining is: " + depthRemaining.ToString());
            //Console.WriteLine("Move is: " + m.ToString());
            board.MakeMove(m);
            /*
            Recursively call this function again at 1 lower depth, informing the 
            eval function that the color has flipped.
            Here the masking of alpha and beta isolates just the eval bits; this prevents 'downstreaming' 
            effects where calls later in the loop get passed alpha/beta with move bits already set,
            which can lead to awful collisions where the move indices get added together.
            Since the nodes deeper in the tree don't need to know about the move order thus far,
            we just remove it. 
            */
            currentEval = AlphaBeta(board, alpha & BITMASKS[0], beta & BITMASKS[0], depthRemaining - 1, !isMinimizing);
            //Console.WriteLine("Current move index is: " + Convert.ToString((long) moveIndex, 10));

            /*
            if (depthRemaining == 4) {
                Console.WriteLine("Current eval is: " + Convert.ToString((long) currentEval, 2));
                Console.WriteLine("Bounding eval is: " + Convert.ToString((long) boundingEval, 2));
            }
            */
            
            board.UndoMove(m);
            if (currentEval == 0){
                //Console.WriteLine("Continuing");
                continue;
            }
            //The first evaluation returned will have 48 trailing zeroes in its binary representation;
            //zero out the moveIndex in that position, then stick the moveIndex for this move into 
            //that spot in the eval.
            currentEval += moveIndex << ((6 - depthRemaining) * 8);

            
            boundingEval = isMinimizing ? alpha : beta;
            //If either we're minimizing and the search fails low, or maximizing and the search fails high
            if (isMinimizing == currentEval < boundingEval) {
                //Console.WriteLine("Pruning.");
                //Console.WriteLine("Current eval is: " + Convert.ToString((long) currentEval, 2));
                //Console.WriteLine("Bounding eval is: " + Convert.ToString((long) boundingEval, 2));
                return 0;
            } 
            
            //Update bounding eval if we haven't pruned the node: If it's our move, we're trying to find the
            //maximum eval we can get (e.g., the best move for us). If it's our opponent's move, they're
            //trying to find the move with the lowest eval (e.g., their best move, in the sense it puts us in the 
            //worst position). As the loop iterates this will give the best move for each player at a single node.
            beta = isMinimizing ?  Math.Min(currentEval, beta) : beta;
            alpha = isMinimizing ? alpha : Math.Max(currentEval, alpha);
               
        }   
        //Console.WriteLine("Unpruned bounding eval is: " + Convert.ToString((long) boundingEval, 2));
        return isMinimizing ? beta : alpha;
    }

    int Evaluate(Board board, int depthRemaining)
    {
        int eval = 0x7FFF;
        bool isOurMove = isBotWhite == board.IsWhiteToMove;

        if (board.IsInCheckmate())
        {
            //Console.WriteLine("Checkmate check");
            return eval = isOurMove ? 1 : 0xFFF0 + depthRemaining;
        }
        else if (board.IsDraw())
        {
            return eval = 1;
        }
        else { 
            //Difference in material value
            eval += 75 * (MaterialPointValue(board, isBotWhite) - MaterialPointValue(board, !isBotWhite));
            eval += board.GetLegalMoves().Length;
            eval +=  isOurMove != board.IsInCheck() ? 30 : 0;
        }

        return eval;
    }
    //Sums up all the material values for the pieces owned by a given color.
    ushort MaterialPointValue(Board board, bool isWhite)
    {
        int pointVal = 0;
        var pieceValDict = new Dictionary<PieceType, int>()
        { 
            {PieceType.Pawn, 1},
            {PieceType.Knight, 3},
            {PieceType.Bishop, 3},
            {PieceType.Rook, 5},
            {PieceType.Queen, 9},
        };
        foreach(PieceType p in Enum.GetValues(typeof(PieceType)))
        {
            if (p != PieceType.None && p != PieceType.King) {
                //Console.WriteLine(p.ToString());
                pointVal += (board.GetPieceList(p, isWhite)).Count * pieceValDict[p];
            }
        }
        return (ushort) pointVal;
    }
}