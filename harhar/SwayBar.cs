using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Harhar
{
    class SwayBar : AbstractBar
    {
        string bar;
        string pointer;
        string blankPointer;
        int counter;
        direction currdir;
        enum direction { right, left };
        public SwayBar()
            : base()
        {
            this.bar = "|                         |";
            this.pointer = "***";
            this.blankPointer = this.BlankPointer();
            this.currdir = direction.right;
            this.counter = 1;
        }

        /// <summary>
        /// sets the atribute blankPointer with a empty string the same length that the pointer
        /// </summary>
        /// <returns>A string filled with space characters</returns>
        private string BlankPointer()
        {
            StringBuilder blank = new StringBuilder();
            for (int cont = 0; cont < this.pointer.Length; cont++)
                blank.Append(" ");
            return blank.ToString();
        }

        /// <summary>
        /// reset the bar to its original state
        /// </summary>
        private void ClearBar()
        {
            this.bar = this.bar.Replace(this.pointer, this.blankPointer);
        }

        /// <summary>
        /// remove the previous pointer and place it in a new possition
        /// </summary>
        /// <param name="start">start index</param>
        /// <param name="end">end index</param>
        private void PlacePointer(int start, int end)
        {
            this.ClearBar();
            this.bar = this.bar.Remove(start, end);
            this.bar = this.bar.Insert(start, this.pointer);
        }

        /// <summary>
        /// prints the progress bar acorrding to pointers and current direction
        /// </summary>
        public override void Step()
        {
            if (this.currdir == direction.right)
            {
                this.PlacePointer(counter, this.pointer.Length);
                this.counter++;
                if (this.counter + this.pointer.Length == this.bar.Length)
                    this.currdir = direction.left;
            }
            else
            {
                this.PlacePointer(counter - this.pointer.Length, this.pointer.Length);
                this.counter--;
                if (this.counter == this.pointer.Length)
                    this.currdir = direction.right;
            }
            Console.Write(this.bar + "\r");
        }
    }
}
