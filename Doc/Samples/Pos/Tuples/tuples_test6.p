main machine Entry {
    var m:id;

    start state init {
        entry {
            m = new Foo();
        }
    }
}

machine Foo {
    var a:((int, int),int);

    start state dummy {
        entry {
            a = ((1,2), 3);
        }
    }
}