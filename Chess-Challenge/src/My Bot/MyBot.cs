using ChessChallenge.API;
using System;
using System.Collections.Generic;
using System.Linq;

public class MyBot : IChessBot
{
    public Move Think(Board board, Timer timer)
    {
        ulong test = AlphaSearch(board, 0, 3);
        Console.WriteLine(Convert.ToString((long) test, 2));

        Move[] moves = board.GetLegalMoves();
        int moveIndex = (int) (((test << 32) >> 56) - 1);
        Console.WriteLine(board.GetLegalMoves()[moveIndex].ToString());
        return board.GetLegalMoves()[moveIndex];
    }
    /*
    Initial depthRemaining value must be odd for this simplified search to work.
    Essentially, by only ever looking at odd-ply variations, we effectively run just the
    "alpha" half of the alpha-beta algorithm. This hurts our search efficiency, but is 
    hugely more token efficient since we can run everything with just one function.
    Likewise the fact the eval is always positive lets us use max/min uniformly, saving 
    tokens by using a bunch of ternary operators. 

    The returned ulong is being used as a "register", with the following structure in bits:
        [0000 0000 0000 0000] [0000 0000] [0000 0000] [0000 0000] [0000 0000] [0000 0000]  [0000 0000]
            Eval (16 bits)       Move 5      Move 4      Move 3      Move 2     Move 1      (Padding)
    Each 8-bit move represents the index of that move in the board.getLegalMoves() list. This is
    more efficient than storing the 12-bit move itself, since the maximum number of legal moves
    in any reachable chess position seems to be < 256; thus, we can fit 5 moves in a single ulong.

    This is done for pruning purposes (see below) and to let us return the principal variation
    simultaneously with the eval. Any comparisons of the eval will be faithful to the comparisons
    between the actual eval shorts since the eval is the first 16 bits and thus dominates the value
    of the ulong. With this optimization, we can run an alpha-beta search with only one implemented function,
    and don't have to write a separate RootSearch() function either, greatly reducing token usage in exchange
    for making this eldritch abomination.
    */
    ulong AlphaSearch(Board board, ulong boundingEval, int depthRemaining) {
        //Console.WriteLine("boundingEval is " + boundingEval.ToString());
        ulong currentEval = 0;
        if(depthRemaining==0){
            //Console.WriteLine("Board evaluation is: " + Convert.ToString(Evaluate(board)));
            //Console.WriteLine("Shifted board evaluation is: " + Convert.ToString((long) (((ulong) Evaluate(board)) << 48), 2));
            return ((ulong) Evaluate(board)) << 48;
        }
        //Odd depths are our moves, even depth are our opponents'. Alpha search will work by finding
        //the move that maximizes our eval on our turn, then finding the move for our opponent 
        //that minimizes the best evals we can reach. 
        bool isMaxing = depthRemaining%2==1;
        ulong moveIndex = 0;
        foreach(Move m in board.GetLegalMoves()){
            moveIndex++;
            //Console.WriteLine("Depth remaining is: " + depthRemaining.ToString());
            //Console.WriteLine("Move is: " + m.ToString());
            board.MakeMove(m);
            //The first evaluation returned will have 48 trailing zeroes in its binary representation;
            //stick the moveIndex for this move into its spot in the eval.
            currentEval = AlphaSearch(board, boundingEval, depthRemaining - 1) + (moveIndex << (6 - depthRemaining) * 8);
            //Console.WriteLine("Current move index is: " + Convert.ToString((long) moveIndex, 2));
            //Console.WriteLine("Current eval is: " + Convert.ToString((long) currentEval, 2));
            board.UndoMove(m);

            /*
            Nodes will will never be pruned by alpha-beta if they share the a (2n + 1)th parent, e.g.,
            if 2n + 1 ply before each move, we were in the same position. Thus, with depth limited to 5 ply,
            any depth 5 node will have a "parent" at depth 4 and a "great-grandparent" at depth 2.

            This means that we'll have
                [16 eval bits] [8 bits: This move] [8 bits: Same move 4] [8 bits: Same move 3] etc.
            meaning the last 40 bits will be the same if the two nodes share a parent. If they share
            a great-grandparent, they'll share the last 24 bits. 
            
            Note that before the first search completes, 
            these bits will all be zero, since we haven't looped back through the recursion, but by 
            definition for how the search works, they have to be filled in before the parent or
            great-grandparent comparison becomes relevant, so it's fine.

            By avoiding pruning under this condition, we can reuse the bounding eval repeatedly, getting
            us our principal variation essentially for free in terms of token count since we don't have to
            implement a separate root search. 
            */
        
            //Shifting by 64 - 40 = 24 and 64 - 24 = 40 and comparing determines if the bounding eval
            //was set by an appropriately related node. Note that being a "first cousin" (e.g., sharing
            //a node 2 levels above in the tree) DOES allow pruning, so testing for the shared 
            //great-grandparent is not sufficient.
            bool isSibling = currentEval << 24 == boundingEval << 24;
            bool isSecondCousin = currentEval << 40 == boundingEval << 40 & !(currentEval << 32 == boundingEval << 32);
            if (isMaxing && currentEval > boundingEval && !(isSibling || isSecondCousin)) {
                Console.WriteLine("Pruning.");
                return boundingEval;
            } 
            /*
            If this is the first time visiting this node (and it hasn't been pruned, since we got to this
            step to begin with), then accept whatever value we get from the first full-depth search. 
            It looks like this just erases the boundingEval we've worked so hard to calculate, but either
            A) This node connects to the best move, in which case we will overwrite it with the
            best boundingEval we'll be generated; or
            B) It doesn't, in which case the best boundingEval generated somewhere else will eventually replace it.
            */
            boundingEval = moveIndex==1 ? currentEval : boundingEval;

            //Update bounding eval if we haven't pruned the node: If it's our move, we're trying to find the
            //maximum eval we can get (e.g., the best move for us). If it's our opponent's move, they're
            //trying to find the move with the lowest eval (e.g., their best move, in the sense it puts us in the 
            //worst position). As the loop iterates this will give the best move for each player at a single node.
            boundingEval = isMaxing ? Math.Max(currentEval, boundingEval) : Math.Min(currentEval, boundingEval);
               
        }   
        Console.WriteLine("Bounding eval is: " + Convert.ToString((long) boundingEval, 2));
        return boundingEval;
    }
    
    //ushort Evaluate(Board board) {
        //return (ushort) board.GetLegalMoves().Length;
    //}

    int Evaluate(Board board)
    {
        int eval = 0;

        //Obviously if the player to move is in checkmate, this is the worst possible outcome for them, so return max negative value.
        if (board.IsInCheckmate())
        {
            eval = 0;
        }
        else if (board.IsDraw())
        {
            eval = 0;
        }
        else { 
            //Difference in material value
            eval = 1000 + MaterialPointValue(board, !board.IsWhiteToMove) - MaterialPointValue(board, board.IsWhiteToMove);
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