using ChessChallenge.API;
using System;
using System.Collections.Generic;
using System.Linq;

public class MyBot : IChessBot
{
    public Move Think(Board board, Timer timer)
    {
        int depth = 2;
        ulong startingVal = 0b1111111111111111000000010000000000000000000000000000000000000000;
        ulong test = HalfBetaSearch(board, startingVal, depth, board.IsWhiteToMove);
        //Console.WriteLine(Convert.ToString((long) test, 2));

        List<Move> principalVariation = new List<Move>();
        List<int> moveIndices = new List<int>();
        int index = 0;
        for (int i = depth + 1; i>=2; i--) {
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
        Move[] moves = b.GetLegalMoves();
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

    bool isFirstUniqueMoveAtOddMoveNumber(ulong eval1, ulong eval2) {
        //Console.WriteLine(Convert.ToString((long) eval1,2));
        //Console.WriteLine(Convert.ToString((long) eval2,2));
        ulong andedEval = eval1 & eval2;
        //Console.WriteLine(Convert.ToString((long) andedEval,2));
        int j = 1;
        //If the first eight bits are zero, the two moves at that byte were the same;
        //break if we've searched all 6 moves. 
        while((andedEval & 63ul) == 0 && j < 7) {
            //If they were the same, continue shifting right, which puts the next ANDed move
            //in the first eight bits of andedEval
            andedEval>>=8;
            //Console.WriteLine(Convert.ToString((long) andedEval, 2));
            //Increment our counter to keep track of oddness/evenness
            j++;
        }
        //If the first eight bits weren't zero, the jth move was not the same, so immediately
        //return whether j was even or odd
        //Console.WriteLine(j);
        return j%2==1;
    }

    /*
    Initial depthRemaining value must be even for this simplified search to work.
    Essentially, by only ever looking at even-ply variations, we effectively run just the
    "beta" half of the alpha-beta algorithm. This hurts our search efficiency, but is 
    hugely more token efficient since we can run everything with just one function.
    Likewise the fact the eval is always positive lets us use max/min uniformly, saving 
    tokens by using a bunch of ternary operators. 

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
    ulong HalfBetaSearch(Board board, ulong boundingEval, int depthRemaining, bool isOurColorWhite) {
        //Console.WriteLine("boundingEval is " + boundingEval.ToString());
        Move[] legalMoves = board.GetLegalMoves();

        //Odd depths are our moves, even depth are our opponents'. Beta search will work by finding
        //the move that minimizes our eval on our opponent's turn, then finding the move for ourselves 
        //that maximizes the best evals we can reach. 
        bool isMinimizing = depthRemaining%2==1;

        ulong moveIndex = 0;
        ulong currentEval = 0;

        if(depthRemaining==0 || legalMoves.Length == 0 || board.IsDraw()){
            //Console.WriteLine("Board evaluation is: " + Convert.ToString(Evaluate(board)));
            //Console.WriteLine("Shifted board evaluation is: " + Convert.ToString((long) (((ulong) Evaluate(board)) << 48), 2));
            return ((ulong) Evaluate(board, isOurColorWhite) ) << 48;
        }

        foreach(Move m in legalMoves){
            moveIndex++;
            Console.WriteLine("Depth remaining is: " + depthRemaining.ToString());
            //Console.WriteLine("Move is: " + m.ToString());
            board.MakeMove(m);
            //The first evaluation returned will have 48 trailing zeroes in its binary representation;
            //stick the moveIndex for this move into its spot in the eval.
            currentEval = HalfBetaSearch(board, boundingEval, depthRemaining - 1, isOurColorWhite) + (moveIndex << ((6 - depthRemaining) * 8));
            //Console.WriteLine("Current move index is: " + Convert.ToString((long) moveIndex, 2));

            /*
            if (depthRemaining == 3) {
                Console.WriteLine("Current eval is: " + Convert.ToString((long) currentEval, 2));
                Console.WriteLine("Bounding eval is: " + Convert.ToString((long) boundingEval, 2));
            }
            */
            board.UndoMove(m);

            /*
            Nodes will will never be pruned by alpha-beta if they share the a (2n + 1)th parent, e.g.,
            if 2n + 1 ply before each move, we were in the same position. Thus, with depth limited to 6 ply,
            any depth 6 node will have a "parent" at depth 5, a "great-grandparent" at depth 3,
            and a "great-great-grandparent" at depth 1.

            This means that we'll have
                [16 eval bits] [8 bits: This move] [8 bits: Same move 4] [8 bits: Same move 3] etc.
            meaning the last 40 bits will be the same if the two nodes share a parent. If they share
            a great-grandparent, they'll share the last 24 bits, and if they share a great-great-great-grandparent,
            they'll share the last 8. 
            
            Note that before the first search completes, 
            these bits will all be zero, since we haven't looped back through the recursion, but by 
            definition for how the search works, they have to be filled in before the parent or
            great-grandparent comparison becomes relevant, so it's fine.

            By avoiding pruning under this condition, we can reuse the bounding eval repeatedly, getting
            us our principal variation essentially for free in terms of token count since we don't have to
            implement a separate root search. 
            
        
            Shifting by 64 - 40 = 24, 64 - 24 = 40, and 64 - 8 = 56 and comparing would determine if the bounding eval
            was set by an appropriately related node. Note that being a "first cousin" (e.g., sharing
            a node 2 levels above in the tree) or a "third cousin" (e.g., sharing a node 4 levels above) 
            DOES allow pruning, so testing for the shared great-grandparent, etc. is not sufficient.

            However, if you work all the truth tables out, it turns out you prune iff the *first* unique
            move separating the two nodes was at odd move count, e.g., move 1, move 3, or move 5.

            A final note: The overwhelming majority of nodes are pruneable. In general, the ratio of unpruneable
            nodes to the total number of nodes, given a branching ratio b and a depth d is 
                r = (b + (b^3 - b^2) + (b^5 - b^4) + ... + (b^n - b^(n-1))/b^d
            where n = d if d odd, n = d - 1 if d even. Each term gives the number of jth cousins. The numerator is
            an alternating power series, e.g., 
                numerator = -sum_{j = 1}^n (-b)^(j) = b/(1+b) * (1-(-b)^n) = b/(1+b) (1 + b^n)
            since n is always odd. This gives
                r = b/(1 + b) (1 + b^n)/b^d
            For a binary tree, with depth d= 6, we have b = 2 and n = 5; this gives r = 0.343.
            But in chess, b is more like 30, so 
                r ~= 0.0323
            This looks familiar, because in the limit of large b, and for d even, we have
                r ~ 1/b
            So only 1 in 30 or so nodes is unpruneable. Note that the other 29 won't necessarily be pruned--the
            conditions on the eval still have to be met--but that was true for the normal alpha-beta algorithm too.
            Thus, this simplification to the alpha-beta algorithm doesn't meaningfully affect its performance; 
            the original has only 3.3% advantage over BetaSearch() in pruning efficiency. 
            This is the magic of this algorithm: Somehow, it gets *better* as the branching ratio increases 
            (relative to the original, at least)--in chess, we'll only take a marginal performance hit even though we've
            deleted half the pruning algorithm! And if the branching ratio drops low enough that we would perform noticeably
            worse, well, we probably don't need the performance anyway. An acceptable tradeoff, I think.

            A final-er note: This minor efficiency hit becomes major if you try odd depths rather than even ones. 
            In this case the asymptotic limit is
                r ~ (b-1)/b
            rather than r ~ 1/b, since now moves that share only the starting position aren't pruneable any longer. 
            Since this accounts for an overwhelming majority of nodes, almost everything becomes unpruneable and 
            we lose all the benefits of alpha-beta, so I'll stick to even depths only here.
            */
            bool isSibling = currentEval << 24 == boundingEval << 24;
            //bool isNotSecondCousin = !(currentEval << 40 == boundingEval << 40 & !(currentEval << 32 == boundingEval << 32));
            //bool isNotSiblingOrSecondCousin = isNotSibling || isNotSecondCousin;

            bool is1st3rdOr5thCousin = isFirstUniqueMoveAtOddMoveNumber(currentEval, boundingEval);
            
            //Console.WriteLine("isNotSiblingOrSecondCousin is: " + isNotSiblingOrSecondCousin.ToString());
            //Console.WriteLine("is1st3rdOr5thCousin is: " + is1st3rdOr5thCousin.ToString());
            //Console.WriteLine("");
            /*
            if (isMinimizing && currentEval < boundingEval && isFirstUniqueMoveAtOddMoveNumber(currentEval, boundingEval)) {
                //Console.WriteLine("Pruning.");
                //Console.WriteLine("Bounding eval is: " + Convert.ToString((long) boundingEval, 2));
                //Console.WriteLine("Current eval is: " + Convert.ToString((long) currentEval, 2));
                return boundingEval;
            } 
            */
                   
            if (isMinimizing && currentEval < boundingEval && !isSibling) {
                //Console.WriteLine("Pruning.");
                Console.WriteLine("Current eval is: " + Convert.ToString((long) currentEval, 2));
                Console.WriteLine("Bounding eval is: " + Convert.ToString((long) boundingEval, 2));
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
            //Console.WriteLine(Convert.ToString((long) boundingEval,2));

            //Update bounding eval if we haven't pruned the node: If it's our move, we're trying to find the
            //maximum eval we can get (e.g., the best move for us). If it's our opponent's move, they're
            //trying to find the move with the lowest eval (e.g., their best move, in the sense it puts us in the 
            //worst position). As the loop iterates this will give the best move for each player at a single node.
            boundingEval = isMinimizing ? Math.Min(currentEval, boundingEval) : Math.Max(currentEval, boundingEval);
               
        }   
        //Console.WriteLine("Unpruned bounding eval is: " + Convert.ToString((long) boundingEval, 2));
        return boundingEval;
    }
    
    //ushort Evaluate(Board board) {
        //return (ushort) board.GetLegalMoves().Length;
    //}

    int Evaluate(Board board, bool isOurColorWhite)
    {
        int eval = 1000;
        bool isOurMove = isOurColorWhite == board.IsWhiteToMove && board.IsInCheck();

        if (board.IsInCheckmate())
        {
            eval = isOurColorWhite == board.IsWhiteToMove ? 0 : 10000;
        }
        else if (board.IsDraw())
        {
            eval = 0;
        }
        else { 
            //Difference in material value
            eval += 75 * (MaterialPointValue(board, isOurColorWhite) - MaterialPointValue(board, !isOurColorWhite));
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