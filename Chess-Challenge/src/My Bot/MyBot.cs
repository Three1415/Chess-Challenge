using ChessChallenge.API;
using System;
using System.Collections.Generic;
using System.Linq;

public class MyBot : IChessBot
{
    public Move Think(Board board, Timer timer)
    {
        Move[] moves = board.GetLegalMoves();
        return moves[0];
    }
    /*
    Initial depthRemaining value must be odd for this simplified search to work.
    Essentially, by only ever looking at odd-ply variations, we effectively run just the
    "alpha" half of the alpha-beta algorithm. This hurts our search efficiency, but is 
    hugely more token efficient since we can run everything with just one function.
    Likewise the fact the eval is always positive lets us use max/min uniformly, saving 
    tokens by using a bunch of ternary operators. 

    The returned ulong is being used as a micro registry, with the following structure:
        [0000 0000 0000 0000] [0000 0000] [0000 0000] [0000 0000] [0000 0000] [000000]
                 Eval            Move 5  Move 4   Move 3   Move 2    Move 1   (Padding)
    Each 8-bit move represents the index of the move in the board.getLegalMoves() list. This is
    more efficient than storing the 12-bit move itself, since the maximum number of legal moves
    in any reachable chess position seems to be < 256; thus, we can fit 5 moves in a single ulong.

    This is done for pruning purposes (see below) and to let us return the principal variation
    simultaneously with the eval. Any comparisons of the eval will be faithful to the comparisons
    between the actual eval shorts since the eval is the first 16 bits and thus dominates the value
    of the ulong. 
    */
    ulong AlphaSearch(Board board, ulong boundingEval, int depthRemaining) {
        ulong currentEval = 0;
        if(depthRemaining==0){
            return ((ulong) Evaluate(board)) << 48;
        }
        //Odd depths are our moves, even depth are our opponents'. Alpha search will work by finding
        //the move that maximizes our eval on our turn, then finding the move for our opponent 
        //that minimizes the best evals we can reach. 
        bool isMaxing = depthRemaining%2==1;
        ulong moveIndex = 0;
        foreach(Move m in board.GetLegalMoves()){
            board.MakeMove(m);
            //The first evaluation returned will have 48 trailing zeroes in its binary representation;
            //stick the moveIndex for this move into its spot in the eval.
            currentEval = AlphaSearch(board, boundingEval, depthRemaining - 1) + (moveIndex << (6 - depthRemaining) * 8);
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
            //was set by a related node; prune only if neither was true.
            if (isMaxing && currentEval > boundingEval && !(currentEval << 24 == boundingEval << 24 || currentEval << 40 == boundingEval << 40)) {
                return boundingEval;
            } 

            //If this is the first time visiting this node and it hasn't been pruned, then
            //accept whatever value we get from the first full-depth search. It looks like
            //this just erases the boundingEval we've worked so hard to calculate, but either
            //A) This node connects to the best move, in which case we will overwrite it with the
            //best boundingEval we'll be generated; or
            //B) It doesn't, in which case the best boundingEval generated somewhere else will eventually replace it.
            boundingEval = (moveIndex==0 && boundingEval == 0)  ? currentEval : boundingEval;

            //Update bounding eval if we haven't pruned the node: If it's our move, we're trying to find the
            //maximum eval we can get (e.g., the best move for us). If it's our opponent's move, they're
            //trying to find the move with the lowest eval (e.g., their best move, in the sense it puts us in the 
            //worst position). As the loop iterates this will give the best move for each player at a single node.
            boundingEval = isMaxing ? Math.Max(currentEval, boundingEval) : Math.Min(currentEval, boundingEval);
            moveIndex++;    
        }

        return boundingEval;
    }
    
    ushort Evaluate(Board board) {
        return 0;
    }
}