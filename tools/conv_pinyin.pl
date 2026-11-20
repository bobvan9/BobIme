use strict;
use warnings;
use open qw(:std :encoding(UTF-8));

my $dst = shift @ARGV;
open my $out, '>', $dst or die "open $dst: $!";

my (%weight, %order);
for my $src (@ARGV) {
    open my $in, '<', $src or die "open $src: $!";
    while (my $line = <$in>) {
        next if $line =~ /^#/;
        chomp $line;
        my @f = split /\t/, $line;
        next if @f < 2;
        my ($word, $code, $weight) = ($f[0], join('', split / /, $f[1]), $f[2] // 1);

        # 只留真正能敲的：纯字母拼音、常用长度、有一定使用频率。
        next if @f == 3 && $weight < 100;
        next if $code !~ /^[a-z]{1,16}$/;
        next if length($word) > 8;

        # 同一个字的不同读音都保留，同形同码只留权重高的那条。
        my $key = "$word\t$code";
        $weight{$key} = $weight if !exists $weight{$key} || $weight > $weight{$key};
        $order{$key} = keys(%weight) + 1 if !exists $order{$key};
    }
    close $in;
}

my $kept = 0;
for my $key (sort { $weight{$b} <=> $weight{$a} } keys %weight) {
    my ($word, $code) = split /\t/, $key;
    print {$out} "$word\t$code\t$weight{$key}\n";
    $kept++;
}
print "kept $kept entries\n";
